using System.Collections.Generic;

namespace UnityPackageImporter.Models;

internal enum ImportActionBlocker
{
    StationDisabled, MissingItem, AlreadyCreated, PreviousCreationFailed,
    OutfitRequiresInstallAction, MissingResult, StructureNotReady, RequiresInstallation, NativeNotReady
}

internal sealed record ImportActionGate(ImportOutputAction Action, IReadOnlyList<ImportActionBlocker> Blockers)
{
    internal bool Enabled => Blockers.Count == 0;
}

internal static class ImportActionPolicy
{
    internal static ImportOutputAction Intended(bool isAvatar, bool isOutfit) =>
        isOutfit ? ImportOutputAction.InstallOutfit : isAvatar ? ImportOutputAction.CreateAvatar : ImportOutputAction.KeepObject;

    internal static string Label(ImportOutputAction action) => action == ImportOutputAction.CreateAvatar ? "createAvatar" : "keepInWorld";

    internal static bool CanPerformMainAction(ImportResult result, bool isAvatar, bool isOutfit) =>
        !isOutfit && result != null && result.CanPerform(Intended(isAvatar, isOutfit));

    internal static ImportActionGate EvaluateMainAction(ImportResult result, bool isAvatar, bool isOutfit,
        bool actionsEnabled, bool hasItem, bool alreadyCreated, bool previousCreationFailed)
    {
        var blockers = new List<ImportActionBlocker>();
        var action = Intended(isAvatar, isOutfit);
        if (!actionsEnabled) blockers.Add(ImportActionBlocker.StationDisabled);
        if (!hasItem) blockers.Add(ImportActionBlocker.MissingItem);
        if (alreadyCreated) blockers.Add(ImportActionBlocker.AlreadyCreated);
        if (previousCreationFailed) blockers.Add(ImportActionBlocker.PreviousCreationFailed);
        if (isOutfit) blockers.Add(ImportActionBlocker.OutfitRequiresInstallAction);
        if (result == null) blockers.Add(ImportActionBlocker.MissingResult);
        else
        {
            if (!result.StructureReady) blockers.Add(ImportActionBlocker.StructureNotReady);
            if (result.RequiresInstallation && action != ImportOutputAction.InstallOutfit) blockers.Add(ImportActionBlocker.RequiresInstallation);
            if (action == ImportOutputAction.CreateAvatar && result.NativeReadiness != NativeAvatarReadiness.Ready)
                blockers.Add(ImportActionBlocker.NativeNotReady);
        }
        return new(action, blockers.AsReadOnly());
    }
}
