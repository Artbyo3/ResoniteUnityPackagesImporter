using System.Globalization;
using UnityPackageImporter.Models;

internal static class VrcFuryInitialAppearanceTests
{
    private const string MaterialGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ClipGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("VRCF initial TurnOn establishes off default even when defaultOn is false", () =>
        {
            var result = Evaluate(Graph());
            check(result.CanApply && !result.ActiveSelf[3] && result.Writes.Single().Active == false, Describe(result));
        });
        foreach (bool defaultOn in new[] { false, true })
        foreach (bool invert in new[] { false, true })
        foreach (int mode in new[] { 0, 1 })
        {
            test($"VRCF Toggle defaultOn={defaultOn} invertRestLogic={invert} mode={mode}", () =>
            {
                var result = Evaluate(Graph(flags: $"defaultOn: {Bit(defaultOn)}, invertRestLogic: {Bit(invert)}, includeInRest: 1", actionData: $"version: 1, obj: {{fileID: 3}}, mode: {mode}"));
                bool on = defaultOn ^ invert;
                check(result.CanApply && result.ActiveSelf[3] == (mode == 0 ? on : !on), Describe(result));
                check(result.Diagnostics.Any(item => item.Code.EndsWith("OBSOLETE_INCLUDE_IN_REST", StringComparison.Ordinal)), "Obsolete includeInRest must be explicitly ignored, not used as defaultOn.");
            });
        }
        test("VRCF legacy ObjectToggle v0 ignores serialized mode and upgrades to Toggle", () =>
        {
            var upload = Evaluate(Graph("ApplyDuringUpload", actionData: "version: 0, mode: 0, obj: {fileID: 3}"));
            var toggle = Evaluate(Graph(actionData: "mode: 0, obj: {fileID: 3}"));
            check(upload.CanApply && !upload.ActiveSelf[3], Describe(upload));
            check(toggle.CanApply && toggle.ActiveSelf[3], "Missing action version upgrades to v0 Toggle then BuildOff restores authored state.");
            var negative = Evaluate(Graph("ApplyDuringUpload", actionData: "version: -1, mode: 0, obj: {fileID: 3}"));
            check(negative.CanApply && !negative.ActiveSelf[3], "Upstream deserialization normalizes negative serialized versions to zero before upgrading.");
        });
        test("VRCF nested managed State resolves without executing orphan actions", () =>
        {
            string component = Graph().Replace("state: {actions: [{rid: 1002}]}", "state: {rid: 1001}", StringComparison.Ordinal) +
                Entry(1001, "State", "VF.Model", "{actions: [{rid: 1002}]}") +
                Entry(9999, "ObjectToggleAction", "VF.Model.StateAction", "{version: 1, mode: 1, obj: {fileID: 4}}");
            var result = Evaluate(component);
            check(result.CanApply && !result.ActiveSelf[3] && !result.Writes.Any(item => item.TargetId == 4), Describe(result));
        });
        test("VRCF content and legacy config.features both execute in upstream phase order", () =>
        {
            string component = Graph("ApplyDuringUpload", actionData: "version: 1, obj: {fileID: 3}, mode: 1")
                .Replace("  content:", "  config: {features: [{rid: 2000}]}\n  content:", StringComparison.Ordinal) +
                Entry(2000, "Toggle", "VF.Model.Feature", "{version: 3, defaultOn: 1, state: {actions: [{rid: 2002}]}}") +
                Entry(2002, "ObjectToggleAction", "VF.Model.StateAction", "{version: 0, obj: {fileID: 3}}");
            var result = Evaluate(component);
            check(result.CanApply && result.ActiveSelf[3], Describe(result));
        });
        test("VRCF ApplyDuringUpload ignores absent external animator verification", () =>
        {
            var result = Evaluate(Graph("ApplyDuringUpload", actionData: "version: 1, obj: {fileID: 3}, mode: 1"));
            check(result.CanApply && !result.ActiveSelf[3], Describe(result));
        });
        test("VRCF BlendShape allRenderers stays in owning avatar and copies snapshots", () =>
        {
            var context = Context();
            var result = VrcFuryInitialAppearance.Evaluate(Scene(Graph("ApplyDuringUpload", "BlendShapeAction", "version: 0, blendShape: Smile, blendShapeValue: 72.5")), context);
            check(result.CanApply && result.Writes.Count == 2 && result.Writes.All(item => item.Effect == InitialAppearanceEffect.Shape && item.TargetId != 90), Describe(result));
            check(result.Renderers[30].Shapes["Smile"] == 72.5f && context.Renderers[30].Shapes["Smile"] == 20, "Compiler must leave input snapshots untouched.");
            check(!result.Renderers.ContainsKey(90), "Other avatar must not enter the output snapshot.");
        });
        test("VRCF BlendShape selected renderer and authored fallback are exact", () =>
        {
            string fields = "allRenderers: 0, renderer: {fileID: 30}, blendShape: Smile, blendShapeValue: 45";
            var off = Evaluate(Graph("Toggle", "BlendShapeAction", fields));
            var on = Evaluate(Graph("Toggle", "BlendShapeAction", fields, "defaultOn: 1"));
            check(off.CanApply && off.Writes.Count == 0 && off.Renderers[30].Shapes["Smile"] == 20, Describe(off));
            check(on.CanApply && on.Writes.Single().TargetId == 30 && on.Renderers[40].Shapes["Smile"] == 30, Describe(on));
        });
        test("VRCF numeric fields are culture invariant", () =>
        {
            var before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var result = Evaluate(Graph("ApplyDuringUpload", "BlendShapeAction", "blendShape: Smile, blendShapeValue: 12.5"));
                check(result.CanApply && result.Writes.All(item => item.Weight == 12.5f), Describe(result));
            }
            finally { CultureInfo.CurrentCulture = before; }
        });
        foreach (string target in new[] { "{fileID: 10}", "{fileID: 999}", "{fileID: 3, guid: cccccccccccccccccccccccccccccccc}" })
        {
            test("VRCF exact ObjectToggle target rejects foreign missing or external " + target, () =>
            {
                var result = Evaluate(Graph("ApplyDuringUpload", actionData: "version: 1, obj: " + target));
                check(!result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code.EndsWith("TARGET", StringComparison.Ordinal)), Describe(result));
            });
        }
        foreach (string flag in new[] { "slider", "hasTransition", "securityEnabled", "separateLocal", "useGlobalParam", "enableDriveGlobalParam", "enableExclusiveTag", "exclusiveOffState", "hasExitTime", "holdButton" })
        {
            test("VRCF unsupported dynamic Toggle skips entire feature " + flag, () =>
            {
                var result = Evaluate(Graph(flags: flag + ": 1, defaultOn: 1"));
                check(result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code.EndsWith("DYNAMIC", StringComparison.Ordinal) && item.Message.Contains(flag, StringComparison.Ordinal)), Describe(result));
            });
        }
        foreach (string flag in new[] { "desktopActive", "androidActive", "localOnly", "remoteOnly" })
        {
            test("VRCF unsupported conditional action does not infer host target " + flag, () =>
            {
                var result = Evaluate(Graph("ApplyDuringUpload", actionData: "version: 1, obj: {fileID: 3}, " + flag + ": 1"));
                check(result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code.EndsWith("ACTION_FILTER", StringComparison.Ordinal)), Describe(result));
            });
        }
        test("VRCF malformed duplicate missing and cyclic managed references fail closed", () =>
        {
            foreach (string component in new[]
            {
                Graph() + Entry(1002, "ObjectToggleAction", "VF.Model.StateAction", "{version: 1, obj: {fileID: 4}}"),
                Graph().Replace("rid: 1002}]", "rid: 12345}]", StringComparison.Ordinal),
                Graph().Replace("state: {actions: [{rid: 1002}]}", "state: {rid: 1000}", StringComparison.Ordinal),
                Graph().Replace("rid: 1002}]", "rid: nonsense}]", StringComparison.Ordinal)
            })
            {
                var result = Evaluate(component);
                check(!result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Severity == InitialAppearanceSeverity.Error), Describe(result));
            }
        });
        test("VRCF conflicts across simultaneous features and within State are diagnosed", () =>
        {
            string component = Graph("ApplyDuringUpload").Replace("  content:", "  config: {features: [{rid: 2000}]}\n  content:", StringComparison.Ordinal) +
                Entry(2000, "ApplyDuringUpload", "VF.Model.Feature", "{action: {actions: [{rid: 2002}]}}") +
                Entry(2002, "ObjectToggleAction", "VF.Model.StateAction", "{version: 1, mode: 1, obj: {fileID: 3}}");
            var across = Evaluate(component);
            var inside = Evaluate(Graph("ApplyDuringUpload").Replace("actions: [{rid: 1002}]", "actions: [{rid: 1002}, {rid: 2002}]", StringComparison.Ordinal) + Entry(2002, "ObjectToggleAction", "VF.Model.StateAction", "{version: 1, mode: 1, obj: {fileID: 3}}"));
            check(new[] { across, inside }.All(item => !item.CanApply && item.Writes.Count == 0 && item.Diagnostics.Any(diagnostic => diagnostic.Code.EndsWith("CONFLICT", StringComparison.Ordinal))), "Ambiguous simultaneous assignments must not publish partial writes.");
        });
        test("VRCF destructive unknown and unsupported features retain actionable diagnostics", () =>
        {
            var destructive = Evaluate(Graph("ApplyDuringUpload", "DeleteObjectAction", "obj: {fileID: 3}"));
            var unknown = Evaluate(Graph("ApplyDuringUpload", "MysteryAction", ""));
            var feature = Evaluate(Graph("DeleteMesh"));
            check(destructive.Writes.Count == 0 && destructive.Diagnostics.Any(item => item.Code.EndsWith("DESTRUCTIVE_SKIPPED", StringComparison.Ordinal) && item.Message.Contains("DeleteObjectAction", StringComparison.Ordinal)), Describe(destructive));
            check(unknown.Diagnostics.Any(item => item.Message.Contains("MysteryAction", StringComparison.Ordinal)) && feature.Diagnostics.Any(item => item.Message.Contains("DeleteMesh", StringComparison.Ordinal)), "Unknown types must remain actionable, not disappear.");
        });
        test("VRCF serialized future versions and malformed fields are not guessed", () =>
        {
            foreach (string fields in new[] { "version: 2, obj: {fileID: 3}", "version: 1, mode: 99, obj: {fileID: 3}", "version: 1, obj: {fileID: 3}, localOnly: []" })
            {
                var result = Evaluate(Graph("ApplyDuringUpload", actionData: fields));
                check(!result.CanApply && result.Writes.Count == 0, Describe(result));
            }
        });
        RegisterAssets(test, check);
        test("VRCF empty authored State and saved initial pose remain explicitly bounded", () =>
        {
            var empty = Evaluate(Graph().Replace("state: {actions: [{rid: 1002}]}", "state: {}", StringComparison.Ordinal));
            var saved = Evaluate(Graph(flags: "saved: 1"));
            check(empty.CanApply && empty.Writes.Count == 0, Describe(empty));
            check(saved.CanApply && !saved.ActiveSelf[3] && saved.Diagnostics.Any(item => item.Code.EndsWith("SAVED_RUNTIME_UNEVALUATED", StringComparison.Ordinal)), Describe(saved));
        });
        test("VRCF malformed YAML and pre-cancelled evaluation are bounded", () =>
        {
            var malformed = VrcFuryInitialAppearance.Evaluate("not composed Unity yaml", Context());
            check(!malformed.CanApply && malformed.Diagnostics.Any(item => item.Code.EndsWith("YAML", StringComparison.Ordinal)), Describe(malformed));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { VrcFuryInitialAppearance.Evaluate(Scene(Graph()), Context(), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Compiler must honor cancellation before returning a write set.");
        });
    }

    private static void RegisterAssets(Action<string, Action> test, Action<bool, string> check)
    {
        test("VRCF material wrapper exact id modern objRef and legacy migration", () =>
        {
            foreach (string wrapper in new[]
            {
                "{version: 1, id: '" + MaterialGuid + ":2100000|ignored/path|ignored name'}",
                "{version: 1, id: 'bad:22', objRef: {fileID: 2100000, guid: " + MaterialGuid + "}}",
                "{version: 0, guid: " + MaterialGuid + ", fileID: 2100000}"
            })
            {
                var result = Evaluate(Graph("ApplyDuringUpload", "MaterialAction", "version: 1, renderer: {fileID: 30}, materialIndex: 1, mat: " + wrapper));
                check(result.CanApply && result.Writes.Single().Material == new InitialAppearanceMaterial(2100000, MaterialGuid) && result.Renderers[30].Materials[1].Guid == MaterialGuid, Describe(result));
            }
            var legacy = Evaluate(Graph("ApplyDuringUpload", "MaterialAction", "version: 0, obj: {fileID: 3}, mat: {version: 1, id: '" + MaterialGuid + ":2100000'}"));
            check(legacy.CanApply && legacy.Writes.Single().TargetId == 30, Describe(legacy));
        });
        test("VRCF material capacity and cross avatar validation are preflight failures", () =>
        {
            foreach (string fields in new[] { "renderer: {fileID: 30}, materialIndex: 2", "renderer: {fileID: 90}" })
            {
                var result = Evaluate(Graph("ApplyDuringUpload", "MaterialAction", "version: 1, " + fields + ", mat: {version: 1, id: '" + MaterialGuid + ":2100000'}"));
                check(!result.CanApply && result.Writes.Count == 0, Describe(result));
            }
        });
        test("VRCF clip resolver receives exact identity and consumes real static clip parser", () =>
        {
            string yaml = Scene(Graph("ApplyDuringUpload", "AnimationClipAction", "clip: {version: 1, id: '" + ClipGuid + ":7400000|name is not identity'}"));
            string? selectedGuid = null;
            long selectedId = 0;
            var result = VrcFuryInitialAppearance.Evaluate(yaml, Context(), (guid, id) =>
            {
                selectedGuid = guid;
                selectedId = id;
                return UnityStaticAnimationClip.Parse(Clip("Body", 1, "m_IsActive", 0), id);
            });
            check(result.CanApply && !result.ActiveSelf[3] && selectedGuid == ClipGuid && selectedId == 7400000, Describe(result));
            var unsupported = VrcFuryInitialAppearance.Evaluate(yaml, Context());
            check(unsupported.CanApply && unsupported.Writes.Count == 0 && unsupported.Diagnostics.Any(item => item.Code.EndsWith("CLIP_UNSUPPORTED", StringComparison.Ordinal)), Describe(unsupported));
        });
        test("VRCF exact clip paths preserve ancestor rewrite without leaf borrowing", () =>
        {
            foreach (string path in new[] { "Body" })
            {
                var result = ClipEvaluate(path, 137, "blendShape.Smile", 66);
                check(result.CanApply && result.Writes.Single().TargetId == 30 && result.Writes.Single().Weight == 66, Describe(result));
            }
            var missing = ClipEvaluate("OtherAvatar/Body", 1, "m_IsActive", 0);
            var escape = ClipEvaluate("../../OtherAvatar/Body", 1, "m_IsActive", 0);
            check(!missing.CanApply && !escape.CanApply && missing.Writes.Count == 0 && escape.Writes.Count == 0, "Paths must never escape or borrow a matching leaf in another avatar.");
            check(!ClipEvaluate("/Body", 1, "m_IsActive", 0).CanApply && !ClipEvaluate("../Body", 1, "m_IsActive", 0).CanApply, "The strict static clip parser intentionally rejects absolute/dot-parent bindings even though the pinned path rewriter supports them.");
        });
        test("VRCF variable clip and unresolved clip never produce partial writes", () =>
        {
            string yaml = Scene(Graph("ApplyDuringUpload", "AnimationClipAction", "clip: {version: 1, id: '" + ClipGuid + ":7400000'}"));
            var missing = VrcFuryInitialAppearance.Evaluate(yaml, Context(), (_, _) => null);
            var variable = VrcFuryInitialAppearance.Evaluate(yaml, Context(), (_, id) => UnityStaticAnimationClip.Parse(Clip("Body", 1, "m_IsActive", 0).Replace("m_Curve: [{time: 0, value: 0}]", "m_Curve: [{time: 0, value: 0}, {time: 1, value: 1}]", StringComparison.Ordinal), id));
            check(!missing.CanApply && !variable.CanApply && missing.Writes.Count == 0 && variable.Writes.Count == 0, Describe(variable));
        });
    }

    private static InitialAppearanceResult ClipEvaluate(string path, int classId, string attribute, float value) =>
        VrcFuryInitialAppearance.Evaluate(Scene(Graph("ApplyDuringUpload", "AnimationClipAction", "clip: {version: 1, id: '" + ClipGuid + ":7400000'}")), Context(), (_, id) => UnityStaticAnimationClip.Parse(Clip(path, classId, attribute, value), id));
    private static string Clip(string path, int classId, string attribute, float value) =>
        "--- !u!74 &7400000\nAnimationClip:\n  m_FloatCurves:\n  - curve:\n      m_Curve: [{time: 0, value: " + value.ToString(CultureInfo.InvariantCulture) + "}]\n    path: '" + path + "'\n    classID: " + classId + "\n    attribute: '" + attribute + "'\n";
    private static InitialAppearanceResult Evaluate(string component) => VrcFuryInitialAppearance.Evaluate(Scene(component), Context());
    private static int Bit(bool value) => value ? 1 : 0;
    private static string Describe(InitialAppearanceResult result) => string.Join("; ", result.Diagnostics.Select(item => item.Code + ": " + item.Message));
    private static InitialAppearanceContext Context() => new()
    {
        AvatarRootId = 1,
        Renderers = new Dictionary<long, InitialAppearanceRenderer>
        {
            [30] = new(3, new Dictionary<string, float> { ["Smile"] = 20 }, new[] { new InitialAppearanceMaterial(2100000, "old"), new InitialAppearanceMaterial(2100000, "old2") }),
            [40] = new(4, new Dictionary<string, float> { ["Smile"] = 30 }, Array.Empty<InitialAppearanceMaterial>()),
            [90] = new(10, new Dictionary<string, float> { ["Smile"] = 99 }, Array.Empty<InitialAppearanceMaterial>())
        }
    };
    private static string Graph(string feature = "Toggle", string action = "ObjectToggleAction", string actionData = "version: 1, obj: {fileID: 3}, mode: 0", string flags = "") =>
        "--- !u!114 &200\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + "}\n  version: 3\n  content: {rid: 1000}\n  references:\n    version: 2\n    RefIds:\n" +
        Entry(1000, feature, "VF.Model.Feature", "{version: " + (feature == "Toggle" ? 3 : 0) + ", " + (feature == "Toggle" ? "state" : "action") + ": {actions: [{rid: 1002}]}" + (flags.Length == 0 ? "" : ", " + flags) + "}") +
        Entry(1002, action, "VF.Model.StateAction", "{" + actionData + "}");
    private static string Entry(long identity, string type, string space, string data) =>
        "    - rid: " + identity + "\n      type: {class: " + type + ", ns: " + space + ", asm: VRCFury}\n      data: " + data + "\n";
    private static string Scene(string component) =>
        Node(1, 0, "Avatar", true) + Node(2, 1, "Host", true) + Node(3, 1, "Body", true) + Node(4, 1, "BodyTwo", false) +
        Node(9, 0, "OtherAvatar", true) + Node(10, 9, "Body", true) +
        "--- !u!137 &30\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 3}\n--- !u!137 &40\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 4}\n--- !u!137 &90\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 10}\n" + component;
    private static string Node(long identity, long parent, string name, bool active) =>
        "--- !u!1 &" + identity + "\nGameObject:\n  m_Name: " + name + "\n  m_IsActive: " + Bit(active) + "\n--- !u!4 &" + (100 + identity) + "\nTransform:\n  m_GameObject: {fileID: " + identity + "}\n  m_Father: {fileID: " + (parent == 0 ? 0 : 100 + parent) + "}\n";
}
