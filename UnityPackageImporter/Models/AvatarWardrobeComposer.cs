#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace UnityPackageImporter.Models;

public static class AvatarWardrobeComposer
{
    internal static AvatarWardrobePlan Compose(
        string yaml,
        InitialAppearanceContext context,
        Func<string, long, UnityStaticClipResult?>? clipResolver = null,
        VrcMenu? baseMenu = null,
        IReadOnlyDictionary<string, VrcParameter>? baseParameters = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();

        var diagnostics = new List<InitialAppearanceDiagnostic>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Error) =>
            diagnostics.Add(new(severity, "WARDROBE_COMPOSER_" + code, message, source));

        var mixedContext = new InitialAppearanceContext
        {
            AvatarRootId = context.AvatarRootId,
            AuthoredHierarchy = context.AuthoredHierarchy,
            Renderers = context.Renderers,
            ExternalAnimationAndParameterContextVerifiedAbsent = false,
            AllowMixedFrameworks = true
        };


        var vrcfPlan = VrcFuryInitialAppearance.CompileWardrobe(yaml, mixedContext, clipResolver, token);


        var maPlan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, mixedContext, token);


        diagnostics.AddRange(vrcfPlan.Diagnostics);
        diagnostics.AddRange(maPlan.Diagnostics);

        var controls = new List<AvatarWardrobeControl>();
        controls.AddRange(maPlan.Controls);
        controls.AddRange(vrcfPlan.Controls);

        if (controls.Count == 0 && (baseMenu == null || baseMenu.Controls.Count == 0))
        {
            return new AvatarWardrobePlan
            {
                Controls = Array.Empty<AvatarWardrobeControl>(),
                Diagnostics = diagnostics.AsReadOnly(),
                HasUnsupportedContext = diagnostics.Any(d => d.Severity != InitialAppearanceSeverity.Info)
            };
        }


        var usedPaths = new HashSet<string>(StringComparer.Ordinal);
        var submenus = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var validControls = new List<AvatarWardrobeControl>();

        foreach (var control in controls)
        {
            token.ThrowIfCancellationRequested();
            if (!identities.Add(control.Id))
            {
                Report("IDENTITY_CONFLICT", $"Duplicate control identity '{control.Id}'.", 0);
                continue;
            }

            string[] parts = control.Path.Split('/');
            if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
            {
                Report("PATH_INVALID", $"Control path '{control.Path}' is invalid.", 0);
                continue;
            }

            string groupPath = "";
            foreach (string part in parts.SkipLast(1))
            {
                groupPath = groupPath.Length == 0 ? part : groupPath + "/" + part;
                submenus.Add(groupPath);
            }

            if (!usedPaths.Add(control.Path))
            {
                Report("PATH_COLLISION", $"Multiple controls share identical path '{control.Path}'.", 0);
                continue;
            }

            validControls.Add(control);
        }


        foreach (var control in validControls)
        {
            if (submenus.Contains(control.Path))
            {
                Report("PATH_SUBMENU_COLLISION", $"Control path '{control.Path}' collides with a submenu container.", 0);
            }
        }



        var propertyWriters = new Dictionary<AvatarPropertyKey, AvatarWardrobeControl>();
        var propertyConflicts = new HashSet<AvatarPropertyKey>();

        foreach (var control in validControls)
        {
            token.ThrowIfCancellationRequested();
            foreach (var binding in control.Bindings)
            {
                var key = AvatarPropertyKey.Of(binding.On);
                if (propertyWriters.TryGetValue(key, out var existing))
                {
                    propertyConflicts.Add(key);
                    Report("PROPERTY_CONFLICT",
                        $"Multiple controls ('{existing.Path}' [{existing.Id}] and '{control.Path}' [{control.Id}]) claim independent drive of target property ({key.Effect}, {key.TargetId}, '{key.Property}').", 0);
                }
                else
                {
                    propertyWriters.Add(key, control);
                }
            }
        }


        bool hasErrors = diagnostics.Any(d => d.Severity == InitialAppearanceSeverity.Error) || propertyConflicts.Count > 0;
        AvatarWardrobeGraph? sharedState = null;
        if (!hasErrors && validControls.Count > 0)
        {
            try
            {
                sharedState = AvatarWardrobeGraph.FromUnifiedControls(
                    context.AvatarRootId.ToString(CultureInfo.InvariantCulture), validControls, baseParameters, token);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                Report("GRAPH_CONSTRUCTION", "Failed to build unified avatar wardrobe graph: " + ex.Message, 0);
                hasErrors = true;
            }
        }

        return new AvatarWardrobePlan
        {
            Controls = validControls.AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly(),
            HasUnsupportedContext = hasErrors,
            SharedState = hasErrors ? null : sharedState
        };
    }
}
