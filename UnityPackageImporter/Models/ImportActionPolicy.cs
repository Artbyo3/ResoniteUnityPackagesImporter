namespace UnityPackageImporter.Models;

internal static class ImportActionPolicy
{
    internal static ImportOutputAction Intended(bool isAvatar, bool isOutfit) =>
        isOutfit ? ImportOutputAction.InstallOutfit : isAvatar ? ImportOutputAction.CreateAvatar : ImportOutputAction.KeepObject;

    internal static string Label(ImportOutputAction action) => action == ImportOutputAction.CreateAvatar ? "createAvatar" : "keepInWorld";

    internal static bool CanPerformMainAction(ImportResult result, bool isAvatar, bool isOutfit) =>
        !isOutfit && result != null && result.CanPerform(Intended(isAvatar, isOutfit));
}
