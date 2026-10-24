#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public static partial class VrcFuryInitialAppearance
{
    internal static AvatarWardrobePlan CompileWardrobe(string yaml, InitialAppearanceContext context,
        CancellationToken token = default)
        => CompileWardrobe(yaml, context, null, token);

    internal static AvatarWardrobePlan CompileWardrobe(string yaml, InitialAppearanceContext context,
        Func<string, long, UnityStaticClipResult?>? clipResolver, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        var controls = new List<AvatarWardrobeControl>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Error) =>
            diagnostics.Add(new(severity, "VRCFURY_WARDROBE_" + code, message, source));
        AvatarWardrobePlan Result()
        {
            bool unsupported = diagnostics.Any(item => item.Severity != InitialAppearanceSeverity.Info);
            return new()
            {
                Controls = controls.AsReadOnly(), Diagnostics = diagnostics.AsReadOnly(), HasUnsupportedContext = unsupported,
                SharedState = !unsupported && controls.Count > 0
                    ? AvatarWardrobeGraph.FromIndependentControls(context.AvatarRootId.ToString(CultureInfo.InvariantCulture), controls, token) : null
            };
        }
        Dictionary<long, Document> documents;
        try { documents = Read(yaml, token); }
        catch (Exception error) when (error is InvalidDataException or YamlException or FormatException or OverflowException or ArgumentException)
        {
            Report("YAML", "Malformed composed Unity YAML: " + error.Message);
            return Result();
        }
        if (!documents.Values.Any(item => item.ClassId == 114 &&
            AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(Text(Get(item.Body, "m_Script") as YamlMappingNode, "guid"))))
            return Result();
        var objects = documents.Values.Where(item => item.ClassId == 1).ToDictionary(item => item.Id);
        var parents = new Dictionary<long, long>();
        foreach (var transform in documents.Values.Where(item => item.ClassId is 4 or 224))
        {
            token.ThrowIfCancellationRequested();
            long owner = Reference(transform.Body, "m_GameObject");
            long father = Reference(transform.Body, "m_Father");
            if (!objects.ContainsKey(owner) || parents.ContainsKey(owner))
            { Report("HIERARCHY", "Missing or duplicate Transform ownership.", transform.Id); continue; }
            if (father != 0 && (!documents.TryGetValue(father, out var parent) || parent.ClassId is not (4 or 224)))
            { Report("HIERARCHY", "Missing parent Transform.", transform.Id); continue; }
            parents.Add(owner, father == 0 ? 0 : Reference(documents[father].Body, "m_GameObject"));
        }
        bool Descends(long candidate, long ancestor)
        {
            var seen = new HashSet<long>();
            while (candidate != 0 && seen.Add(candidate))
            {
                if (candidate == ancestor) return true;
                candidate = parents.GetValueOrDefault(candidate);
            }
            return false;
        }
        bool Within(long identity) => objects.ContainsKey(identity) && Descends(identity, context.AvatarRootId);
        var ownedWrappers = new HashSet<long>();
        foreach (var document in documents.Values.Where(item => item.ClassId == 114))
        {
            token.ThrowIfCancellationRequested();
            string script = Text(Get(document.Body, "m_Script") as YamlMappingNode, "guid");
            if (!AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(script)) continue;
            long owner = Reference(document.Body, "m_GameObject");
            if (!objects.ContainsKey(owner) || Get(document.Body, "m_GameObject") is not YamlMappingNode ownerReference ||
                Text(ownerReference, "guid").Trim('0').Length != 0)
            {
                Report("SCOPE", "VRCFury wrapper has no exact local GameObject owner.", document.Id);
                continue;
            }
            if (!Within(owner)) continue;
            if (IsComponent(document) && Reference(document.Body, "m_Script") != 11500000)
            {
                Report("WRAPPER", "VRCFury wrapper requires an exact script GUID/fileID.", document.Id);
                continue;
            }
            ownedWrappers.Add(document.Id);
            if (!IsComponent(document) && !script.Equals(AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid, StringComparison.OrdinalIgnoreCase))
                Report("WRAPPER", "Alternate VRCFury serialization requires a verified runtime adapter.", document.Id);
        }
        if (ownedWrappers.Count == 0) return Result();
        if (!objects.ContainsKey(context.AvatarRootId)) Report("SCOPE", "An exact owning avatar root is required.");
        foreach (long identity in objects.Keys.Where(Within))
        {
            var seen = new HashSet<long>();
            long current = identity;
            while (current != 0)
            {
                if (!seen.Add(current)) { Report("HIERARCHY", "Cyclic Transform hierarchy.", identity); break; }
                if (!parents.TryGetValue(current, out current))
                { Report("HIERARCHY", "An owned GameObject has no Transform.", identity); break; }
            }
        }
        var features = new List<Feature>();
        ReadFeatures(documents.Where(item => ownedWrappers.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value),
            Within, features, Report, token, includeAll: true);
        if (!features.Any(item => item.Class == "Toggle")) return Result();
        if (!context.ExternalAnimationAndParameterContextVerifiedAbsent && !context.AllowMixedFrameworks)
            Report("CONTEXT", "External controllers, parameters and reactive context must be verified absent.");
        var reactiveGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ModularAvatarInitialAppearance.ObjectToggleGuid, ModularAvatarInitialAppearance.ShapeChangerGuid,
            ModularAvatarInitialAppearance.MaterialSetterGuid, ModularAvatarInitialAppearance.MenuItemGuid,
            ModularAvatarInitialAppearance.MergeAnimatorGuid, AvatarPackageIndex.MenuInstallerScriptGuid,
            AvatarPackageIndex.ParametersScriptGuid, AvatarPackageIndex.BlendshapeSyncScriptGuid
        };
        foreach (var document in documents.Values.Where(item => Within(Reference(item.Body, "m_GameObject"))))
        {
            string script = Text(Get(document.Body, "m_Script") as YamlMappingNode, "guid");
            string identifier = Text(document.Body, "m_EditorClassIdentifier");
            long scriptFileId = Reference(document.Body, "m_Script");
            bool structural = scriptFileId == 11500000 && (script.Equals(AvatarPackageIndex.MergeArmatureScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.BoneProxyScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.OutfitRootScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid, StringComparison.OrdinalIgnoreCase));
            bool descriptor = scriptFileId == AvatarPackageIndex.VrcAvatarDescriptorScriptFileId &&
                script.Equals(AvatarPackageIndex.VrcAvatarDescriptorScriptGuid, StringComparison.OrdinalIgnoreCase);
            bool HasReference(YamlNode? node) => node is YamlMappingNode reference &&
                (ReferenceNode(reference) != 0 || Text(reference, "guid").Trim('0').Length != 0);
            if (descriptor && !context.AllowMixedFrameworks &&
                (HasReference(Get(document.Body, "expressionParameters")) ||
                    Sequence(document.Body, "baseAnimationLayers").Concat(Sequence(document.Body, "specialAnimationLayers"))
                        .OfType<YamlMappingNode>().Any(layer => HasReference(Get(layer, "animatorController")))))
                Report("CONTEXT", "Descriptor parameters/controllers require the shared Animator compiler.", document.Id);
            if (!context.AllowMixedFrameworks && (document.ClassId is 95 or 111 || reactiveGuids.Contains(script) ||
                (!structural && identifier.Contains("ModularAvatar", StringComparison.Ordinal))))
                Report("CONTEXT", "Animator, legacy Animation or MA runtime context is not compiled by independent wardrobe controls.", document.Id);
            if (document.ClassId == 114 && !IsComponent(document) && !structural && !descriptor &&
                (!context.AllowMixedFrameworks || (!reactiveGuids.Contains(script) && !identifier.Contains("ModularAvatar", StringComparison.Ordinal))))
                Report("CONTEXT", "MonoBehaviour runtime interactions have not been verified absent for this exact script GUID/fileID.", document.Id);
            if (AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(script) &&
                !script.Equals(ScriptGuid, StringComparison.OrdinalIgnoreCase) &&
                !script.Equals(AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid, StringComparison.OrdinalIgnoreCase))
                Report("CONTEXT", "Alternate VRCFury wrappers require a verified runtime adapter.", document.Id);
        }
        foreach (var feature in features.Where(item => item.Class != "Toggle" && item.Class != "ArmatureLink"))
            Report("CONTEXT", "Coexisting feature '" + feature.Class + "' has uncompiled runtime/resting interactions.", feature.Source);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var animationPaths = new AvatarAnimationPathResolver(context.AvatarRootId,
            objects.Values.Select(item => new AvatarAnimationObject(item.Id, parents.GetValueOrDefault(item.Id), Text(item.Body, "m_Name"))));
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var destinations = new HashSet<(InitialAppearanceEffect, long, string)>();
        var hosts = features.Where(item => item.Class == "Toggle").Select(item => item.Owner).ToArray();
        foreach (var feature in features.Where(item => item.Class == "Toggle"))
        {
            token.ThrowIfCancellationRequested();
            string identity = feature.Source.ToString(CultureInfo.InvariantCulture) + ":" + feature.Rid.ToString(CultureInfo.InvariantCulture);
            string path = Text(feature.Data, "name");
            if (!identities.Add(identity)) Report("IDENTITY", "Duplicate Toggle identity.", feature.Source);
            if (path.Length == 0 || path.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or "..") ||
                !paths.Add(path) || paths.Any(other => other != path &&
                    (other.StartsWith(path + "/", StringComparison.Ordinal) || path.StartsWith(other + "/", StringComparison.Ordinal))))
                Report("PATH", "Toggle paths must be nonempty, unique and cannot collide with a submenu.", feature.Source);
            if (Integer(feature.Data, "version", 0, feature.Source, Report) != 3)
                Report("VERSION", "Runtime Toggle conversion requires pinned serialized version 3.", feature.Source);
            foreach (string flag in new[] { "saved", "slider", "sliderInactiveAtZero", "securityEnabled", "exclusiveOffState",
                "enableExclusiveTag", "hasExitTime", "enableDriveGlobalParam", "separateLocal", "hasTransition",
                "useGlobalParam", "holdButton", "invertRestLogic", "localOnly", "remoteOnly", "desktopActive", "androidActive" })
                if (Boolean(feature.Data, flag, false, feature.Source, Report))
                    Report("OPTION", "Toggle '" + path + "' uses uncompiled option " + flag + ".", feature.Source);
            if (Sequence(feature.Data, "resetPhysbones").Any(item => ReferenceNode(item) != 0) ||
                Get(feature.Data, "resetPhysbones") is { } reset && reset is not YamlSequenceNode)
                Report("OPTION", "Physics reset is not a reversible wardrobe action.", feature.Source);
            bool defaultOn = Boolean(feature.Data, "defaultOn", false, feature.Source, Report);
            var bindings = new List<AvatarWardrobeBinding>();
            void Add(InitialAppearanceWrite off, InitialAppearanceWrite on)
            {
                if (!destinations.Add((on.Effect, on.TargetId, on.Property)))
                    Report("CONFLICT", "Multiple actions/controls write the same terminal property.", feature.Source);
                bindings.Add(new(off, on));
            }
            long Target(YamlMappingNode data, string field)
            {
                if (Get(data, field) is not YamlMappingNode reference ||
                    !long.TryParse(Text(reference, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long target) ||
                    target == 0 || Text(reference, "guid").Trim('0').Length != 0)
                { Report("TARGET", "Action requires an exact nonnull local " + field + " fileID.", feature.Source); return 0; }
                return target;
            }
            foreach (var action in feature.Actions)
            {
                token.ThrowIfCancellationRequested();
                bool desktopActive = Boolean(action.Data, "desktopActive", false, feature.Source, Report);
                bool androidActive = Boolean(action.Data, "androidActive", false, feature.Source, Report);
                if (desktopActive != androidActive)
                    Report("FILTER", "Action platform filtering requires a separate runtime adapter.", feature.Source);
                foreach (string flag in new[] { "localOnly", "remoteOnly" })
                    if (Boolean(action.Data, flag, false, feature.Source, Report))
                        Report("FILTER", "Action platform/local filtering requires a separate runtime adapter.", feature.Source);
                if (action.Class == "ObjectToggleAction")
                {
                    int version = Integer(action.Data, "version", 0, feature.Source, Report);
                    int mode = Integer(action.Data, "mode", 0, feature.Source, Report);
                    long target = Target(action.Data, "obj");
                    if (version != 1 || mode is not (0 or 1))
                        Report("ACTION_VERSION", "Only ObjectToggleAction v1 explicit TurnOn/TurnOff is supported.", feature.Source);
                    if (!Within(target) || target == context.AvatarRootId || hosts.Any(host => Descends(host, target)))
                    { Report("TARGET", "Visibility cannot affect foreign objects, avatar root or Toggle hosts/ancestors.", feature.Source); continue; }
                    if (Text(objects[target].Body, "m_IsActive") is not ("0" or "1"))
                        Report("ACTIVE", "Visibility target has malformed authored m_IsActive.", target);
                    Add(new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: mode != 0),
                        new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: mode == 0));
                }
                else if (action.Class == "BlendShapeAction")
                {
                    if (Integer(action.Data, "version", 0, feature.Source, Report) != 0)
                        Report("ACTION_VERSION", "Only BlendShapeAction v0 is supported.", feature.Source);
                    string shape = Text(action.Data, "blendShape");
                    float weight = 100;
                    if (Get(action.Data, "blendShapeValue") != null &&
                        (!float.TryParse(Text(action.Data, "blendShapeValue"), NumberStyles.Float, CultureInfo.InvariantCulture, out weight) || !float.IsFinite(weight)))
                        Report("WEIGHT", "Blendshape values must be finite.", feature.Source);
                    bool all = Boolean(action.Data, "allRenderers", true, feature.Source, Report);
                    long selected = all ? 0 : Target(action.Data, "renderer");
                    var targets = context.Renderers.Where(item => Within(item.Value.GameObjectId) &&
                        (all || item.Key == selected) && item.Value.Shapes.ContainsKey(shape)).ToArray();
                    if (shape.Length == 0 || targets.Length == 0)
                        Report("SHAPE", "No exact owned renderer/shape was resolved.", feature.Source);
                    foreach (var target in targets)
                    {
                        if (!documents.TryGetValue(target.Key, out var renderer) || renderer.ClassId != 137 ||
                            Reference(renderer.Body, "m_GameObject") != target.Value.GameObjectId || !float.IsFinite(target.Value.Shapes[shape]))
                        { Report("TARGET", "Blendshape snapshot has no exact owned skinned renderer.", feature.Source); continue; }
                        Add(new(InitialAppearanceEffect.Shape, target.Key, shape, Weight: target.Value.Shapes[shape]),
                            new(InitialAppearanceEffect.Shape, target.Key, shape, Weight: weight));
                    }
                }
                else if (action.Class == "MaterialAction")
                {
                    int version = Integer(action.Data, "version", 0, feature.Source, Report);
                    if (version is < 0 or > 1)
                    { Report("ACTION_VERSION", "Only MaterialAction v0/v1 is supported.", feature.Source); continue; }
                    long rendererId;
                    if (version == 0 && Get(action.Data, "obj") is { } objNode)
                    {
                        if (objNode is not YamlMappingNode objMap ||
                            !long.TryParse(Text(objMap, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long owner) ||
                            Text(objMap, "guid").Trim('0').Length != 0)
                        { Report("TARGET", "Legacy MaterialAction.obj has a malformed or external reference.", feature.Source); continue; }
                        if (owner != 0)
                        {
                            var matches = documents.Values.Where(item => item.ClassId is 23 or 137 && Reference(item.Body, "m_GameObject") == owner).ToArray();
                            if (!Within(owner) || matches.Length != 1)
                            { Report("TARGET", "Legacy MaterialAction.obj must select one unambiguous owned Renderer.", feature.Source); continue; }
                            rendererId = matches[0].Id;
                        }
                        else rendererId = Target(action.Data, "renderer");
                    }
                    else rendererId = Target(action.Data, "renderer");
                    if (!context.Renderers.TryGetValue(rendererId, out var renderer) || !Within(renderer.GameObjectId) ||
                        !documents.TryGetValue(rendererId, out var rendererDoc) || rendererDoc.ClassId is not (23 or 137) ||
                        Reference(rendererDoc.Body, "m_GameObject") != renderer.GameObjectId)
                    { Report("TARGET", "MaterialAction Renderer is missing or outside its owning avatar.", feature.Source); continue; }
                    int index = Integer(action.Data, "materialIndex", 0, feature.Source, Report);
                    if (index < 0 || index >= renderer.Materials.Count)
                    { Report("MATERIAL_CAPACITY", "MaterialAction index is outside the captured Renderer material capacity.", feature.Source); continue; }
                    var baseline = renderer.Materials[index];
                    if (baseline == null || baseline.FileId == 0 || !Regex.IsMatch(baseline.Guid ?? "", @"\A[0-9a-fA-F]{32}\z"))
                    { Report("MATERIAL_BASELINE", "MaterialAction requires an exact authored baseline material GUID/fileID to restore the off state.", feature.Source); continue; }
                    var material = AssetReference(Get(action.Data, "mat"), feature.Source,
                        (code, message, source, _) => Report(code, message, source, InitialAppearanceSeverity.Error));
                    if (material == null) continue;
                    string slot = index.ToString(CultureInfo.InvariantCulture);
                    Add(new(InitialAppearanceEffect.Material, rendererId, slot, Material: new(baseline.FileId, baseline.Guid!.ToLowerInvariant())),
                        new(InitialAppearanceEffect.Material, rendererId, slot, Material: material));
                }
                else if (action.Class == "AnimationClipAction")
                {
                    if (Integer(action.Data, "version", 0, feature.Source, Report) != 0)
                        Report("ACTION_VERSION", "Only AnimationClipAction v0 is supported.", feature.Source);
                    var reference = AssetReference(Get(action.Data, "clip"), feature.Source, Report);
                    if (reference == null) continue;
                    if (clipResolver == null)
                    { Report("CLIP_RESOLVER", "Animation actions require exact GUID/fileID clip resolution.", feature.Source); continue; }
                    var clip = clipResolver(reference.Guid, reference.FileId);
                    token.ThrowIfCancellationRequested();
                    if (clip == null)
                    { Report("CLIP_MISSING", "The selected clip cannot be resolved from this package.", feature.Source); continue; }
                    foreach (var diagnostic in clip.Diagnostics)
                        Report("CLIP_" + diagnostic.Code, diagnostic.Message, feature.Source, diagnostic.Severity);
                    if (!clip.CanApply) continue;
                    if (clip.Curves.Count == 0) Report("CLIP_EMPTY", "The clip contains no supported constant bindings.", feature.Source);
                    foreach (var curve in clip.Curves)
                    {
                        token.ThrowIfCancellationRequested();
                        bool visibility = curve.ClassId == 1 && curve.Attribute == "m_IsActive";
                        bool shapeCurve = curve.ClassId == 137 && curve.Attribute.StartsWith("blendShape.", StringComparison.Ordinal);
                        if (!float.IsFinite(curve.Value) || !(visibility || shapeCurve) || visibility && curve.Value is not (0 or 1))
                        { Report("CLIP_BINDING", "Only finite constant visibility and blendshape bindings are supported.", feature.Source); continue; }
                        long target = animationPaths.Resolve(feature.Owner, curve.Path, candidate => curve.ClassId == 1 ||
                            documents.Values.Any(item => item.ClassId == curve.ClassId && Reference(item.Body, "m_GameObject") == candidate));
                        if (target == 0)
                        { Report("CLIP_PATH", "Clip path '" + curve.Path + "' has no unambiguous owned destination.", feature.Source); continue; }
                        if (visibility)
                        {
                            if (target == context.AvatarRootId || hosts.Any(host => Descends(host, target)) ||
                                Text(objects[target].Body, "m_IsActive") is not ("0" or "1"))
                            { Report("TARGET", "Clip visibility cannot affect avatar roots, control hosts or malformed authored states.", feature.Source); continue; }
                            Add(new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: Text(objects[target].Body, "m_IsActive") == "1"),
                                new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: curve.Value == 1));
                        }
                        else
                        {
                            string shape = curve.Attribute.Substring("blendShape.".Length);
                            var nativeSources = documents.Values.Where(item => item.ClassId == 137 && Reference(item.Body, "m_GameObject") == target).ToArray();
                            if (nativeSources.Length != 1 || !context.Renderers.TryGetValue(nativeSources[0].Id, out var renderer) ||
                                renderer.GameObjectId != target || !renderer.Shapes.TryGetValue(shape, out float baseline) || !float.IsFinite(baseline))
                            { Report("CLIP_SHAPE", "Clip blendshape requires one exact skinned renderer and original weight.", feature.Source); continue; }
                            Add(new(InitialAppearanceEffect.Shape, nativeSources[0].Id, shape, Weight: baseline),
                                new(InitialAppearanceEffect.Shape, nativeSources[0].Id, shape, Weight: curve.Value));
                        }
                    }
                }
                else Report("ACTION", "Action '" + action.Class + "' has no faithful wardrobe runtime adapter.", feature.Source);
            }
            if (bindings.Count == 0) Report("EMPTY", "Toggle has no complete supported bindings.", feature.Source);
            string icon = "";
            if (Boolean(feature.Data, "enableIcon", false, feature.Source, Report))
                Report("ICON", "Authored Toggle icon is not converted in this first slice; labels/grouping are retained.", feature.Source, InitialAppearanceSeverity.Info);
            controls.Add(new(identity, path, defaultOn, bindings.AsReadOnly(), icon));
        }
        return Result();
    }
}
