using UnityPackageImporter.Materials;
using UnityPackageImporter.Materials.LilToon;
using UnityPackageImporter.Materials.Poiyomi;
using UnityPackageImporter.Models;

internal static class TextureCompositionTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        void Near(float a, float b) => check(Math.Abs(a - b) < 0.00001f, $"{a} != {b}");
        test("Poiyomi mask-only emission uses the declared channel and inversion", () =>
        {
            var data = new UnityMaterialData();
            data.Textures["_EmissionMask"] = ("mask", 1, 1, 0, 0);
            data.Numbers["_EmissionMaskChannel"] = 2;
            data.Numbers["_EmissionMaskInvert"] = 1;
            var plan = PoiyomiEmissionPlan.Create(data);
            Near(plan.Evaluate(_ => (0.1f, 0.2f, 0.75f, 1)).r, 0.25f);
            data.Numbers["_EmissionMaskInvert"] = 0;
            check(plan.Key != PoiyomiEmissionPlan.Create(data).Key, "Invert state shares a texture cache entry.");
            check(plan.Inputs.Single().Guid == "mask" && plan.UnsupportedReason == null, "Mask-only emission lost its source.");
        });
        test("Black emission mask suppresses all glow and white preserves the emission map", () =>
        {
            var data = new UnityMaterialData();
            data.Textures["_EmissionMap"] = ("color", 1, 1, 0, 0);
            data.Textures["_EmissionMask"] = ("mask", 1, 1, 0, 0);
            var plan = PoiyomiEmissionPlan.Create(data);
            var black = plan.Evaluate(i => i.Guid == "mask" ? (0, 0, 0, 1) : (0.5f, 0.25f, 1, 0.5f));
            Near(black.r, 0); Near(black.b, 0);
            var white = plan.Evaluate(i => i.Guid == "mask" ? (1, 1, 1, 1) : (0.5f, 0.25f, 1, 0.5f));
            Near(white.r, 0.25f); Near(white.g, 0.125f); Near(white.b, 0.5f);
        });
        test("lilToon preserves colored blend-mask RGB and alpha rather than treating it as a red mask", () =>
        {
            var data = new UnityMaterialData();
            data.Textures["_EmissionBlendMask"] = ("mask", 1, 1, 0, 0);
            var c = LilToonEmissionPlan.Create(data).Evaluate(_ => (0.2f, 0.4f, 0.8f, 0.5f));
            Near(c.r, 0.1f); Near(c.g, 0.2f); Near(c.b, 0.4f);
        });
        test("Composition retains a common transformed UV domain without baking away its tiling", () =>
        {
            var data = new UnityMaterialData();
            data.Textures["_EmissionMap"] = ("color", 2, 3, 0.25f, -0.5f);
            data.Textures["_EmissionMask"] = ("mask", 2, 3, 0.25f, -0.5f);
            data.Numbers["_EmissionMapUV"] = 2; data.Numbers["_EmissionMaskUV"] = 2;
            var plan = PoiyomiEmissionPlan.Create(data);
            check(plan.UnsupportedReason == null && plan.Uv == 2 && plan.Inputs[0].ScaleY == 3 && plan.Inputs[0].OffsetY == -0.5f, "Common UV transform lost.");
            data.Numbers["_EmissionMaskUV"] = 1;
            check(PoiyomiEmissionPlan.Create(data).UnsupportedReason != null, "Cross-UV mask silently assumed UV0.");
        });
        test("Composition refuses incompatible transforms, dynamic panning and invalid UV modes", () =>
        {
            var d = new UnityMaterialData();
            d.Textures["_EmissionMap"] = ("color", 1, 1, 0, 0); d.Textures["_EmissionMask"] = ("mask", 2, 1, 0, 0);
            check(PoiyomiEmissionPlan.Create(d).UnsupportedReason != null, "Different transforms accepted.");
            d.Textures.Remove("_EmissionMap"); d.Colors["_EmissionMaskPan"] = (0.1f, 0, 0, 0);
            check(PoiyomiEmissionPlan.Create(d).UnsupportedReason != null, "Animated mask baked as static.");
            d.Colors.Clear(); d.Numbers["_EmissionMaskUV"] = 4;
            check(PoiyomiEmissionPlan.Create(d).UnsupportedReason != null, "Generated UV accepted.");
        });
        test("lilToon mask coordinates include the main texture transform", () =>
        {
            var d = new UnityMaterialData();
            d.Textures["_MainTex"] = ("main", 2, 3, 0.1f, 0.2f);
            d.Textures["_EmissionBlendMask"] = ("mask", 4, 5, 0.3f, 0.4f);
            var input = LilToonEmissionPlan.Create(d).Inputs.Single();
            Near(input.ScaleX, 8); Near(input.ScaleY, 15); Near(input.OffsetX, 0.7f); Near(input.OffsetY, 1.4f);
        });
        test("Texture recipe snapshots do not change when source material dictionaries change", () =>
        {
            var d = new UnityMaterialData(); d.Textures["_EmissionMask"] = ("first", 1, 1, 0, 0);
            var before = PoiyomiEmissionPlan.Create(d); d.Textures["_EmissionMask"] = ("second", 1, 1, 0, 0);
            check(before.Inputs[0].Guid == "first" && before.Key != PoiyomiEmissionPlan.Create(d).Key, "Mutable material changed an existing recipe.");
        });
        test("lilToon non-additive emission is reported rather than silently converted to additive glow", () =>
        {
            var d = new UnityMaterialData(); d.Numbers["_EmissionBlendMode"] = 1;
            check(LilToonEmissionPlan.Create(d).UnsupportedReason == null, "Additive emission rejected.");
            d.Numbers["_EmissionBlendMode"] = 0;
            check(LilToonEmissionPlan.Create(d).UnsupportedReason != null, "Normal blending silently converted to additive.");
        });
        test("Compound texture recovery waits for every input across separate companion imports", () =>
        {
            var pending = new PendingAssetAssignments<object>();
            var available = new HashSet<string>(); var target = new object(); object? assigned = null;
            var output = new object();
            pending.RegisterMany(target, new[] { "map", "mask" },
                () => Task.FromResult(available.Count == 2 ? output : null!),
                (asset, _) => { assigned = asset; return Task.FromResult(true); });
            available.Add("map");
            check(pending.ResolveAsync(new[] { "map" }, default, (_, ex) => throw ex).Result == 0 && assigned == null, "A missing mask became an unmasked output.");
            available.Add("mask");
            check(pending.CanResolveAny(new[] { "mask" }) && pending.ResolveAsync(new[] { "mask" }, default, (_, ex) => throw ex).Result == 1,
                "Second companion did not rebuild the compound field.");
            check(ReferenceEquals(assigned, output) && pending.MissingAssetGuids.Length == 0, "Recovered field retained missing dependencies.");
        });
        test("Rebinding a compound texture supersedes all dependencies of its old recipe", () =>
        {
            var pending = new PendingAssetAssignments<object>(); var target = new object();
            pending.RegisterMany(target, new[] { "old-map", "old-mask" }, () => Task.FromResult(new object()), (_, _) => throw new Exception("Stale recipe applied."));
            pending.RegisterMany(target, new[] { "new-mask" }, () => Task.FromResult(new object()), (_, _) => Task.FromResult(true));
            check(!pending.CanResolveAny(new[] { "old-mask" }), "Superseded mask can still mutate a field.");
            check(pending.ResolveAsync(new[] { "new-mask" }, default, (_, ex) => throw ex).Result == 1, "New recipe failed to resolve.");
        });
    }
}
