using System;
using System.Threading.Tasks;

namespace UnityPackageImporter.UI;

internal sealed record LocalizedPromptText(string English, string Japanese);

internal sealed record DecisionPromptDefinition(
    LocalizedPromptText Title,
    LocalizedPromptText Description,
    LocalizedPromptText Status,
    LocalizedPromptText PrimaryChoice,
    LocalizedPromptText SecondaryChoice,
    LocalizedPromptText DismissChoice);

internal sealed record DecisionPromptKeys(
    string TitleKey,
    string DescriptionKey,
    string StatusKey,
    string PrimaryChoiceKey,
    string SecondaryChoiceKey,
    string DismissChoiceKey);

/// <summary>Reusable content and actions for the station's native two-choice decision prompt.</summary>
internal sealed class ReusableDecisionPrompt
{
    private readonly Action<string, string, string> setLocalized;
    private readonly Action<string, string> setLocalizedKey;
    private readonly Action<bool, bool> setVisibility;
    private Func<Task> primaryAction = Completed;
    private Func<Task> secondaryAction = Completed;
    private Func<Task> dismissAction = Completed;

    internal ReusableDecisionPrompt(Action<string, string, string> setLocalized, Action<bool, bool> setVisibility)
    {
        this.setLocalized = setLocalized ?? throw new ArgumentNullException(nameof(setLocalized));
        this.setVisibility = setVisibility ?? throw new ArgumentNullException(nameof(setVisibility));
    }

    internal ReusableDecisionPrompt(Action<string, string> setLocalizedKey, Action<bool, bool> setVisibility)
    {
        this.setLocalizedKey = setLocalizedKey ?? throw new ArgumentNullException(nameof(setLocalizedKey));
        this.setVisibility = setVisibility ?? throw new ArgumentNullException(nameof(setVisibility));
    }

    internal void Show(
        DecisionPromptKeys keys,
        Func<Task> primary,
        Func<Task> secondary,
        Func<Task> dismiss)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (setLocalizedKey == null) throw new InvalidOperationException("Key-based localization callback was not configured.");
        primaryAction = primary ?? Completed;
        secondaryAction = secondary ?? Completed;
        dismissAction = dismiss ?? Completed;
        setLocalizedKey("waiting", keys.TitleKey);
        setLocalizedKey("companionDescription", keys.DescriptionKey);
        setLocalizedKey("companionMissing", keys.StatusKey);
        setLocalizedKey("importCompanion", keys.PrimaryChoiceKey);
        setLocalizedKey("placeholderMaterials", keys.SecondaryChoiceKey);
        setLocalizedKey("back", keys.DismissChoiceKey);
        setVisibility(true, false);
    }

    internal void Show(
        DecisionPromptDefinition definition,
        Func<Task> primary,
        Func<Task> secondary,
        Func<Task> dismiss)
    {
        ArgumentNullException.ThrowIfNull(definition);
        primaryAction = primary ?? Completed;
        secondaryAction = secondary ?? Completed;
        dismissAction = dismiss ?? Completed;
        setLocalized("waiting", definition.Title.English, definition.Title.Japanese);
        setLocalized("companionDescription", definition.Description.English, definition.Description.Japanese);
        setLocalized("companionMissing", definition.Status.English, definition.Status.Japanese);
        setLocalized("importCompanion", definition.PrimaryChoice.English, definition.PrimaryChoice.Japanese);
        setLocalized("placeholderMaterials", definition.SecondaryChoice.English, definition.SecondaryChoice.Japanese);
        setLocalized("back", definition.DismissChoice.English, definition.DismissChoice.Japanese);
        setVisibility(true, false);
    }

    internal void SetState(bool visible, bool waiting) => setVisibility(visible, waiting);

    internal Task InvokePrimaryAsync() => primaryAction();
    internal Task InvokeSecondaryAsync() => secondaryAction();
    internal Task InvokeDismissAsync() => dismissAction();

    private static Task Completed() => Task.CompletedTask;
}
