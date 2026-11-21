using System.Text.Json;
using System.Text.Json.Nodes;
using UnityPackageImporter.Models;

internal static class InitialAppearanceReplayTests
{
    private const string ClipGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ClipYaml = "--- !u!74 &7400000\nAnimationClip:\n  m_FloatCurves:\n  - path: Body\n    classID: 1\n    attribute: m_IsActive\n    script: {fileID: 0}\n    curve:\n      m_Curve: [{time: 0, value: 0}]\n";

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Appearance replay nested constructor fields cannot silently acquire default state", () =>
        {
            foreach (string field in new[] { "ActiveSelf", "Yaml", "Sha256", "Shapes", "Materials", "GameObjectId" })
            {
                var document = JsonNode.Parse(Create().Serialize())!;
                var owner = field == "ActiveSelf" ? document["Objects"]![0]! : field is "Yaml" or "Sha256" ? document["Clips"]![0]! : document["Renderers"]!["30"]!;
                owner.AsObject().Remove(field);
                bool rejected = false;
                try { InitialAppearanceReplay.Deserialize(document.ToJsonString()).Evaluate(); }
                catch (JsonException) { rejected = true; }
                check(rejected, "Missing nested field was guessed: " + field);
            }
        });
        test("Appearance replay streaming budget rejects excess bytes before writing them", () =>
        {
            using var memory = new MemoryStream();
            using var bounded = new AppearanceReplayByteStream(memory, 16);
            bounded.Write(new byte[8]);
            bool rejected = false;
            try { bounded.Write(new byte[9]); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && memory.Length == 8, "Excess replay bytes reached the destination.");
        });
        test("Appearance replay bounded admission cancels queued writes without invoking them", () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = AppearanceReplayStore.WithWriterAsync(async () => { entered.SetResult(); await release.Task; return "first"; }, CancellationToken.None);
            entered.Task.GetAwaiter().GetResult();
            using var cancellation = new CancellationTokenSource();
            bool cancelledWorkRan = false;
            var second = AppearanceReplayStore.WithWriterAsync(() => { cancelledWorkRan = true; return Task.FromResult("cancelled"); }, cancellation.Token);
            var third = AppearanceReplayStore.WithWriterAsync(() => Task.FromResult("third"), CancellationToken.None);
            var fourth = AppearanceReplayStore.WithWriterAsync(() => Task.FromResult("fourth"), CancellationToken.None);
            try
            {
                bool bounded = false;
                try { AppearanceReplayStore.WithWriterAsync(() => Task.FromResult("overflow"), CancellationToken.None).GetAwaiter().GetResult(); }
                catch (IOException) { bounded = true; }
                cancellation.Cancel();
                bool cancelled = false;
                try { second.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                check(bounded && cancelled && !cancelledWorkRan, "Queued evidence was unbounded or ignored import cancellation.");
            }
            finally { release.TrySetResult(); Task.WhenAll(first, third, fourth).GetAwaiter().GetResult(); }
        });
        test("Appearance replay retention preserves the returned capsule despite future dated evidence", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "appearance-replay-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                for (int index = 0; index < 8; index++)
                {
                    string prior = AppearanceReplayStore.WriteAsync(directory, Create()).GetAwaiter().GetResult();
                    File.SetLastWriteTimeUtc(prior, DateTime.UtcNow.AddDays(1));
                }
                string latest = AppearanceReplayStore.WriteAsync(directory, Create()).GetAwaiter().GetResult();
                check(File.Exists(latest) && Directory.GetFiles(directory).Length == 8, "The reported saved capsule was immediately deleted.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        test("Appearance replay round trips actual hierarchy renderers flags clips and recorded writes", () =>
        {
            var replay = Create();
            var restored = InitialAppearanceReplay.Deserialize(replay.Serialize());
            var result = restored.Evaluate();
            check(restored.MatchesRecorded(result) && result.CanApply && result.Writes.Single().TargetId == 3 && !result.Writes.Single().Active,
                "Replay did not use the exact captured source and clip resolver.");
            check(!restored.ExternalContextVerifiedAbsent && restored.Objects!.Single(item => item.Id == 3).ActiveSelf == false &&
                restored.Renderers[30].Shapes["Smile"] == 25 && restored.Renderers[30].Materials[0].FileId == 2100000,
                "Offline replay replaced captured native context with guessed defaults.");
        });
        test("Appearance replay cannot silently omit a production clip resolver dependency", () =>
        {
            var document = JsonNode.Parse(Create().Serialize())!;
            document["Clips"] = new JsonArray();
            bool failed = false;
            try { InitialAppearanceReplay.Deserialize(document.ToJsonString()).Evaluate(); }
            catch (InvalidDataException) { failed = true; }
            check(failed, "A missing resolver dependency became an unsupported warning and false synthetic success.");
        });
        test("Appearance replay distinguishes captured missing assets from uncaptured dependencies", () =>
        {
            var replay = Create(null);
            var result = InitialAppearanceReplay.Deserialize(replay.Serialize()).Evaluate();
            check(!result.CanApply && result.Diagnostics.Any(item => item.Code == "VRCFURY_INITIAL_CLIP_MISSING") && replay.MatchesRecorded(result),
                "Captured missing source no longer reproduces the hard reference failure.");
        });
        test("Appearance replay rejects changed YAML clip hashes duplicate dependencies and incompatible schemas", () =>
        {
            foreach (string corruption in new[] { "yaml", "clip", "duplicate", "schema", "graph" })
            {
                var document = JsonNode.Parse(Create().Serialize())!;
                if (corruption == "yaml") document["Yaml"] = "changed";
                if (corruption == "clip") document["Clips"]![0]!["Yaml"] = "changed";
                if (corruption == "duplicate") ((JsonArray)document["Clips"]!).Add(document["Clips"]![0]!.DeepClone());
                if (corruption == "schema") document["SchemaVersion"] = 99;
                if (corruption == "graph") document["TransformOwners"] = null;
                bool rejected = false;
                try { InitialAppearanceReplay.Deserialize(document.ToJsonString()).Evaluate(); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "Unverified replay context accepted: " + corruption);
            }
        });
        test("Appearance replay keeps local source contents and does not read changed external files", () =>
        {
            var replay = Create();
            var document = JsonNode.Parse(replay.Serialize())!;
            document["Clips"]![0]!["SourcePath"] = "nonexistent-do-not-read.anim";
            var restored = InitialAppearanceReplay.Deserialize(document.ToJsonString());
            check(restored.Evaluate().CanApply && restored.MatchesRecorded(restored.Evaluate()), "Replay reread mutable package-cache data.");
        });
        test("Appearance replay schema rejects missing required fields unknown properties and cancellation", () =>
        {
            foreach (bool unknown in new[] { false, true })
            {
                var document = JsonNode.Parse(Create().Serialize())!;
                if (unknown) document["AssumeRuntimeAbsent"] = true;
                else document.AsObject().Remove("ExternalContextVerifiedAbsent");
                bool rejected = false;
                try { InitialAppearanceReplay.Deserialize(document.ToJsonString()); }
                catch (JsonException) { rejected = true; }
                check(rejected, "Replay guessed a missing or unrecognized context flag.");
            }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { Create().Evaluate(cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Replay ignored cancellation.");
        });
        test("Appearance replay storage is bounded atomic and excludes unrelated local files", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "appearance-replay-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "keep.json"), "unrelated");
                for (int index = 0; index < 10; index++) AppearanceReplayStore.WriteAsync(directory, Create()).GetAwaiter().GetResult();
                var files = Directory.GetFiles(directory, "appearance-replay-*.json");
                check(files.Length == 8 && File.ReadAllText(Path.Combine(directory, "keep.json")) == "unrelated" && Directory.GetFiles(directory, "*.tmp").Length == 0,
                    "Replay retention removed unrelated data or left partial capsules.");
                check(files.All(file => InitialAppearanceReplay.Deserialize(File.ReadAllText(file)).Evaluate().CanApply), "Stored replay inputs cannot be evaluated.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    private static InitialAppearanceReplay Create(string? clip = ClipYaml)
    {
        string yaml = "--- !u!1 &1 stripped\nGameObject: {}\n--- !u!4 &101 stripped\nTransform: {}\n" +
            "--- !u!1 &2\nGameObject: {m_Name: Host, m_IsActive: 1}\n--- !u!4 &102\nTransform: {m_GameObject: {fileID: 2}, m_Father: {fileID: 101}}\n" +
            "--- !u!1 &3 stripped\nGameObject: {}\n--- !u!4 &103 stripped\nTransform: {}\n" +
            "--- !u!137 &30\nSkinnedMeshRenderer: {m_GameObject: {fileID: 3}}\n" +
            "--- !u!114 &200\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + "}\n  version: 3\n  content: {rid: 1000}\n  references:\n    version: 2\n    RefIds:\n" +
            "    - rid: 1000\n      type: {class: ApplyDuringUpload, ns: VF.Model.Feature, asm: VRCFury}\n      data: {version: 0, action: {actions: [{rid: 1002}]}}\n" +
            "    - rid: 1002\n      type: {class: AnimationClipAction, ns: VF.Model.StateAction, asm: VRCFury}\n      data: {version: 0, clip: {version: 1, id: '" + ClipGuid + ":7400000'}}\n";
        var context = new InitialAppearanceContext
        {
            AvatarRootId = 1,
            AuthoredHierarchy = new AuthoredAppearanceHierarchy(new[] { new AuthoredAppearanceObject(1, 0, "Avatar", true),
                new AuthoredAppearanceObject(2, 1, "Host", true), new AuthoredAppearanceObject(3, 1, "Body", false) },
                new Dictionary<long, long> { [101] = 1, [102] = 2, [103] = 3 }),
            Renderers = new Dictionary<long, InitialAppearanceRenderer> { [30] = new(3, new Dictionary<string, float> { ["Smile"] = 25 }, new[] { new InitialAppearanceMaterial(2100000, ClipGuid) }) },
            ExternalAnimationAndParameterContextVerifiedAbsent = false
        };
        var result = VrcFuryInitialAppearance.Evaluate(yaml, context, (_, identity) => clip == null ? null : UnityStaticAnimationClip.Parse(clip, identity));
        return InitialAppearanceReplay.Capture(yaml, context, new[] { new AppearanceClipInput(ClipGuid, 7400000, "source.anim", clip, clip == null ? null : InitialAppearanceReplay.Hash(clip)) }, result,
            "captured-build", Guid.NewGuid(), "source.unity");
    }
}
