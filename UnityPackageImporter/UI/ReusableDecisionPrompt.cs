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

/// <summary>Reusable content and actions for the station's native two-choice decision prompt.</summary>
internal sealed class ReusableDecisionPrompt
{
    private readonly Action<string, string, string> setLocalized;
    private readonly Action<bool, bool> setVisibility;
    private Func<Task> primaryAction = Completed;
    private Func<Task> secondaryAction = Completed;
    private Func<Task> dismissAction = Completed;

    internal ReusableDecisionPrompt(Action<string, string, string> setLocalized, Action<bool, bool> setVisibility)
    {
        this.setLocalized = setLocalized ?? throw new ArgumentNullException(nameof(setLocalized));
        this.setVisibility = setVisibility ?? throw new ArgumentNullException(nameof(setVisibility));
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
