#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using Renderite.Shared;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

internal static class AvatarVisemeRuntime
{
    internal static IReadOnlyDictionary<IField<float>, float> CaptureWeights(Slot envelope)
    {
        var result = new Dictionary<IField<float>, float>(ReferenceEqualityComparer.Instance);
        foreach (var renderer in envelope.GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>())
            for (int index = 0; index < renderer.BlendShapeWeights.Count; index++)
            {
                var field = renderer.BlendShapeWeights.GetElement(index);
                result[field] = field.Value;
            }
        return result;
    }

    internal static void Apply(PreparedImportItem item, BipedRig rig, Slot envelope,
        IReadOnlySet<DirectVisemeDriver> previousDrivers, IReadOnlySet<VisemeAnalyzer> previousAnalyzers,
        IReadOnlySet<Slot> previousHeadSlots, IReadOnlyDictionary<IField<float>, float> authoredWeights,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        void Report(string message, string code, AvatarOperationSeverity severity = AvatarOperationSeverity.Warning) =>
            item.ConversionPlan.AddDiagnostic(severity, message, code: code);
        var descriptors = item.Manifest.Avatars.Select(definition =>
            (Definition: definition, Root: item.ResolveGameObject(definition.GameObjectFileId)))
            .Where(pair => PreparedImportItem.Contains(envelope, pair.Root) &&
                PreparedImportItem.Contains(pair.Root, rig[BodyNode.Head]) &&
                PreparedImportItem.Contains(pair.Root, rig[BodyNode.Hips])).ToArray();
        if (descriptors.Length == 0) return;
        if (descriptors.Length != 1)
        {
            Report("Multiple owned descriptors make authored lip sync ambiguous; native fallback retained.", "VISEME_DESCRIPTOR_AMBIGUOUS");
            return;
        }
        var descriptor = descriptors[0];
        var settings = descriptor.Definition.Visemes;
        if (settings == null || settings.Mode == null && settings.Error.Length == 0) return;
        IReadOnlyList<AvatarVisemeBinding> bindings;
        UnitySkin skin;
        Slot head;
        VisemeAnalyzer? analyzer;
        DirectVisemeDriver[] generated;
        List<(FieldDrive<float> Drive, IField<float> Field)> oldLinks;
        var targets = new List<(int Channel, IField<float> Field)>();
        try
        {
            if (settings.Error.Length != 0) throw new InvalidDataException(settings.Error);
            if (settings.Mode != 3)
                throw new InvalidDataException("Jaw/default/parameter-only lip-sync modes still require their own native adapters.");
            if (settings.Renderer == null || !item.existingIUnityObjects.TryGetValue(unchecked((ulong)settings.Renderer.FileId), out var entry) ||
                entry is not UnitySkin resolved || resolved.IsStaticMesh || resolved.createdMeshRenderer == null || resolved.createdMeshRenderer.IsDestroyed ||
                resolved.m_GameObject?.TryGetValue("fileID", out ulong owner) != true ||
                !item.existingIUnityObjects.TryGetValue(owner, out var ownerEntry) || ownerEntry is not UnityGameObject game ||
                !PreparedImportItem.Contains(descriptor.Root, game.frooxEngineSlot) ||
                !PreparedImportItem.Contains(game.frooxEngineSlot, resolved.createdMeshRenderer.Slot) ||
                item.existingIUnityObjects.Values.OfType<UnitySkin>().Distinct().Count(candidate => candidate.createdMeshRenderer == resolved.createdMeshRenderer) != 1)
                throw new InvalidDataException("Authored viseme renderer is not an exact uniquely owned skinned renderer.");
            skin = resolved;
            bindings = AvatarVisemePlanner.Compile(settings, skin.SourceBlendShapeNames ?? Array.Empty<string>(), token);
            generated = envelope.GetComponentsInChildren<DirectVisemeDriver>().Where(driver => !previousDrivers.Contains(driver)).ToArray();
            oldLinks = new();
            foreach (var driver in generated)
                for (int channel = 0; channel <= (int)Viseme.Laughter; channel++)
                {
                    var drive = driver[(Viseme)channel];
                    if (drive.Target != null && !drive.IsLinkGranted)
                        throw new InvalidDataException("A native heuristic viseme target has conflicting ownership.");
                    if (drive.Target != null && drive.IsLinkGranted)
                    {
                        if (!authoredWeights.ContainsKey(drive.Target))
                            throw new InvalidDataException("A heuristic viseme destination has no pre-creation appearance snapshot.");
                        oldLinks.Add((drive, drive.Target));
                    }
                }
            foreach (var binding in bindings)
            {
                token.ThrowIfCancellationRequested();
                int index = skin.createdMeshRenderer.BlendShapeIndex(binding.Shape);
                if (index < 0 || index >= skin.createdMeshRenderer.BlendShapeWeights.Count)
                    throw new InvalidDataException("The exact authored viseme is unavailable on the native mesh: " + binding.Shape);
                IField<float> field = skin.createdMeshRenderer.BlendShapeWeights.GetElement(index);
                if ((field.IsDriven || field.IsLinked) && !oldLinks.Any(link => ReferenceEquals(link.Field, field) && link.Drive.IsLinkGranted))
                    throw new InvalidDataException("An authored viseme field is already owned by another native driver: " + binding.Shape);
                targets.Add((binding.Channel, field));
            }
            var heads = envelope.GetComponentsInChildren<IAvatarObject>().Where(node => node.Node == BodyNode.Head)
                .Select(node => node.Slot).Distinct().ToArray();
            if (heads.Length != 1) throw new InvalidDataException("Native lip sync requires one unambiguous avatar head node.");
            head = heads[0];
            if (previousHeadSlots.Contains(head) || head.GetComponentInParents<AvatarRoot>()?.Slot != envelope)
                throw new InvalidDataException("The head node was not created by this native avatar setup for the selected rig.");
            var analyzers = head.GetComponents<VisemeAnalyzer>().ToArray();
            if (analyzers.Length > 1 || analyzers.Any(previousAnalyzers.Contains))
                throw new InvalidDataException("The native analyzer has ambiguous or pre-existing ownership.");
            analyzer = analyzers.SingleOrDefault();
            if (analyzer != null && !head.GetComponents<AvatarVoiceSourceAssigner>().Any(assigner => assigner.TargetReference.Target == analyzer.Source))
                throw new InvalidDataException("The native analyzer has no verified avatar voice-source assigner.");
        }
        catch (InvalidDataException error)
        {
            Report("Authored lip sync was not applied; native fallback retained. " + error.Message, "VISEME_PREFLIGHT_UNSUPPORTED");
            return;
        }
        var rollback = new OwnedAttachmentTransaction<Action>();
        foreach (var link in oldLinks)
            rollback.Track(() =>
            {
                if (!link.Drive.IsLinkGranted && (link.Field.IsDriven || link.Field.IsLinked))
                    throw new InvalidOperationException("Foreign field ownership prevents restoring an original viseme link.");
                if (!link.Drive.IsLinkGranted) link.Drive.ForceLink(link.Field);
                if (!link.Drive.IsLinkGranted || !ReferenceEquals(link.Drive.Target, link.Field))
                    throw new InvalidOperationException("The original native viseme link could not be restored.");
            });
        foreach (var field in oldLinks.Select(link => link.Field).Concat(targets.Select(target => target.Field)).Distinct())
        {
            float before = field.Value;
            rollback.Track(() =>
            {
                if (oldLinks.Any(link => ReferenceEquals(link.Field, field) && link.Drive.IsLinkGranted)) return;
                if (field.IsDriven || field.IsLinked)
                    throw new InvalidOperationException("A viseme field acquired foreign ownership during rollback.");
                field.Value = before;
            });
        }
        void Track(Component component) => rollback.Track(() => { if (!component.IsDestroyed) component.Destroy(); });
        try
        {
            token.ThrowIfCancellationRequested();
            if (analyzer == null)
            {
                analyzer = head.AttachComponent<VisemeAnalyzer>();
                Track(analyzer);
                var assigner = head.AttachComponent<AvatarVoiceSourceAssigner>();
                Track(assigner);
                assigner.TargetReference.Target = analyzer.Source;
            }
            var exact = skin.createdMeshRenderer.Slot.AttachComponent<DirectVisemeDriver>();
            Track(exact);
            exact.Source.Target = analyzer;
            exact.StrengthMultiplier.Value = 1f;
            token.ThrowIfCancellationRequested();
            foreach (var link in oldLinks) link.Drive.Target = null!;
            foreach (var link in oldLinks.Where(link => !targets.Any(target => ReferenceEquals(target.Field, link.Field))))
            {
                if (link.Field.IsDriven || link.Field.IsLinked)
                    throw new InvalidOperationException("An abandoned heuristic viseme destination acquired another driver.");
                link.Field.Value = authoredWeights[link.Field];
            }
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                if (target.Field.IsDriven || target.Field.IsLinked)
                    throw new InvalidOperationException("Native viseme target ownership changed during binding.");
                exact[(Viseme)target.Channel].ForceLink(target.Field);
                if (!exact[(Viseme)target.Channel].IsLinkGranted)
                    throw new InvalidOperationException("The authored viseme drive did not acquire its exact target.");
            }
            Report("Bound " + bindings.Count + " exact authored viseme channels. Jaw/parameter-only modes, duplicate-target mixing and VRChat analyzer equivalence remain pending.",
                "VISEME_AUTHORED_MAPPING_BUILT", AvatarOperationSeverity.Info);
            rollback.Commit();
        }
        catch (Exception failure)
        {
            var errors = rollback.RollBack(action => action());
            if (errors.Count > 0)
                throw new AggregateException("Authored viseme setup failed and rollback reported additional failures.", new[] { failure }.Concat(errors));
            throw;
        }
    }
}
