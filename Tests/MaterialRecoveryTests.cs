using UnityPackageImporter.Models;

internal static class MaterialRecoveryTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Companion retry preserves successful imports and replaces only unsuccessful attempts", () =>
        {
            var cache = new AsyncImportCache<object>();
            var successful = new object();
            var existing = cache.GetOrAdd("existing", () => Task.FromResult(successful));
            var missing = cache.GetOrAdd("missing", () => Task.FromResult<object>(null!));
            var failed = cache.GetOrAdd("failed", () => throw new InvalidDataException("Unreadable asset."));
            var cancelled = cache.GetOrAdd("cancelled", () => Task.FromCanceled<object>(new CancellationToken(true)));
            Check(missing.Result == null && failed.IsFaulted && cancelled.IsCanceled, "The fixture did not complete its unsuccessful attempts.");
            cache.RetryUnsuccessful();
            Check(ReferenceEquals(existing, cache.GetOrAdd("existing", () => throw new Exception("Successful asset reimported."))),
                "Retry discarded a successful shared provider.");
            foreach (var id in new[] { "missing", "failed", "cancelled" })
                Check(ReferenceEquals(cache.GetOrAdd(id, () => Task.FromResult(successful)).Result, successful),
                    "An unsuccessful attempt prevented recovery: " + id);
        });

        test("Companion retry keeps in-flight imports and can retry a late missing result", () =>
        {
            var cache = new AsyncImportCache<object>();
            var completion = new TaskCompletionSource<object>();
            var loading = cache.GetOrAdd("texture", () => completion.Task);
            cache.RetryUnsuccessful();
            Check(ReferenceEquals(loading, cache.GetOrAdd("texture", () => throw new Exception("In-flight import duplicated."))),
                "Companion input duplicated an in-flight import.");
            completion.SetResult(null!);
            Check(loading.Result == null, "Expected the initial missing result.");
            cache.RetryUnsuccessful("texture");
            var recovered = new object();
            Check(ReferenceEquals(recovered, cache.GetOrAdd("texture", () => Task.FromResult(recovered)).Result),
                "A null completing after the companion boundary could not recover.");
        });

        test("Texture-only companion repairs every deferred field with a shared provider", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            var cache = new AsyncImportCache<object>();
            object? available = null;
            object? first = null, second = null;
            int imports = 0;
            Task<object> Load() => cache.GetOrAdd("texture", () => { imports++; return Task.FromResult(available!); });
            Check(Load().Result == null, "Fixture should start with a missing texture.");
            pending.Register(new object(), "texture", Load, (asset, _) => { first = asset; return Task.FromResult(true); });
            pending.Register(new object(), "texture", Load, (asset, _) => { second = asset; return Task.FromResult(true); });
            Check(!pending.CanResolveAny(new[] { "unrelated" }) && pending.CanResolveAny(new[] { "TEXTURE" }),
                "Texture-only companion matching is wrong.");
            available = new object();
            cache.RetryUnsuccessful();
            int restored = pending.ResolveAsync(new[] { "TEXTURE" }, default, Fail).GetAwaiter().GetResult();
            Check(restored == 2 && ReferenceEquals(first, available) && ReferenceEquals(second, available),
                "One of the existing material fields was not rebound.");
            Check(imports == 2 && pending.MissingAssetGuids.Length == 0,
                "Shared texture was loaded repeatedly or dependencies remained after successful assignments.");
        });

        test("Material-first then texture-later stays incomplete until the texture field is assigned", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            var field = new object();
            bool assignmentAllowed = false;
            pending.Register(field, "texture", () => Task.FromResult(new object()), (_, _) => Task.FromResult(assignmentAllowed));
            var missingMaterial = MaterialDependencyDiagnostics.Analyze(new[] { new[] { "material" } }, Array.Empty<string>());
            var materialAvailable = MaterialDependencyDiagnostics.Analyze(Array.Empty<string[]>(), new[] { "material" }, pending.MissingAssetGuids);
            Check(missingMaterial.HasMissingAssets && materialAvailable.HasMissingAssets && materialAvailable.MissingTextureCount == 1,
                "Receiving the material incorrectly completed the texture dependency.");
            Check(pending.ResolveAsync(new[] { "material" }, default, Fail).Result == 0, "A material-only package resolved a missing texture.");
            Check(pending.ResolveAsync(new[] { "texture" }, default, Fail).Result == 0 && pending.MissingAssetGuids.Length == 1,
                "Asset availability was treated as a completed field assignment.");
            assignmentAllowed = true;
            Check(pending.ResolveAsync(new[] { "texture" }, default, Fail).Result == 1, "Later texture assignment did not recover.");
            Check(!MaterialDependencyDiagnostics.Analyze(Array.Empty<string[]>(), new[] { "material", "texture" }, pending.MissingAssetGuids).HasMissingAssets,
                "Completed assignments should report ready.");
        });

        test("A failed deferred texture does not prevent other fields recovering and remains retryable", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            bool fail = true;
            int errors = 0;
            pending.Register(new object(), "bad", () => fail ? throw new InvalidDataException("bad") : Task.FromResult(new object()),
                (_, _) => Task.FromResult(true));
            pending.Register(new object(), "good", () => Task.FromResult(new object()), (_, _) => Task.FromResult(true));
            Check(pending.ResolveAsync(new[] { "bad", "good" }, default, (_, _) => errors++).Result == 1 && errors == 1,
                "Failure prevented independent texture recovery.");
            Check(pending.MissingAssetGuids.SequenceEqual(new[] { "bad" }), "Failed assignment was forgotten.");
            fail = false;
            Check(pending.ResolveAsync(new[] { "bad" }, default, Fail).Result == 1, "Failed assignment could not retry.");
        });

        test("Session cancellation prevents a late texture load from assigning material fields", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            var completion = new TaskCompletionSource<object>();
            using var cancellation = new CancellationTokenSource();
            int assignments = 0;
            pending.Register(new object(), "texture", () => completion.Task,
                (_, _) => { assignments++; return Task.FromResult(true); });
            var resolving = pending.ResolveAsync(new[] { "texture" }, cancellation.Token, Fail);
            cancellation.Cancel();
            completion.SetResult(new object());
            try { resolving.GetAwaiter().GetResult(); throw new Exception("Cancellation was swallowed."); }
            catch (OperationCanceledException) { }
            Check(assignments == 0 && pending.MissingAssetGuids.Length == 1,
                "A cancelled session assigned a late texture or reported it resolved.");
        });

        test("Replacing a deferred field does not apply an obsolete in-flight texture", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            var field = new object();
            var oldTexture = new TaskCompletionSource<object>();
            int oldAssignments = 0, newAssignments = 0;
            pending.Register(field, "old", () => oldTexture.Task,
                (_, _) => { oldAssignments++; return Task.FromResult(true); });
            var oldRequest = pending.ResolveAsync(new[] { "old" }, default, Fail);
            pending.Register(field, "new", () => Task.FromResult(new object()),
                (_, _) => { newAssignments++; return Task.FromResult(true); });
            oldTexture.SetResult(new object());
            Check(oldRequest.Result == 0 && oldAssignments == 0, "An obsolete request overwrote a newer field binding.");
            Check(pending.ResolveAsync(new[] { "new" }, default, Fail).Result == 1 && newAssignments == 1,
                "The new field binding was lost with the obsolete result.");
        });

        test("Optional secondary textures and built-in texture GUIDs do not falsely report missing companion packages", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            var activeTarget = new object();
            var unusedTarget = new object();
            var secondaryTarget = new object();
            bool secondaryRestored = false;

            pending.Register(secondaryTarget, "optional-matcap-guid", () => Task.FromResult(new object()),
                (_, _) => { secondaryRestored = true; return Task.FromResult(true); }, required: false);
            pending.Register(unusedTarget, "unused-fbx-tex-guid", () => Task.FromResult(new object()),
                (_, _) => Task.FromResult(true), required: true);

            Check(pending.GetMissingAssetGuids(target => ReferenceEquals(target, activeTarget)).Length == 0,
                "Unused FBX material or optional secondary texture falsely triggered missing companion prompt.");
            Check(pending.CanResolveAny(new[] { "optional-matcap-guid" }),
                "Optional secondary texture must still be eligible for opportunistic companion resolution.");
            Check(pending.ResolveAsync(new[] { "optional-matcap-guid" }, default, Fail).Result == 1 && secondaryRestored,
                "Optional secondary texture was not restored when provided.");

            string matWithBuiltinTex = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  m_Name: TestMat
  m_Shader: {fileID: 4800000, guid: efa77a80ca0344749b4f19fdd5891cbe, type: 3}
  m_SavedProperties:
    m_TexEnvs:
    - _MainTex:
        m_Texture: {fileID: 10300, guid: 0000000000000000f000000000000000, type: 0}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
";
            var parsedMat = UnityMaterialData.Parse(matWithBuiltinTex);
            Check(parsedMat.Texture("_MainTex") == null, "Unity built-in resource texture GUID must not be treated as a package asset dependency.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Fail(string guid, Exception exception) => throw new Exception("Unexpected failure for " + guid, exception);
}
