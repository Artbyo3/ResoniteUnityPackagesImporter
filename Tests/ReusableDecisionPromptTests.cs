using UnityPackageImporter.UI;

internal static class ReusableDecisionPromptTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Reusable decision prompt replaces localized content and routes the selected action", () =>
        {
            var labels = new Dictionary<string, (string English, string Japanese)>();
            (bool Visible, bool Waiting) state = default;
            string? selected = null;
            var prompt = new ReusableDecisionPrompt(
                (key, english, japanese) => labels[key] = (english, japanese),
                (visible, waiting) => state = (visible, waiting));

            prompt.Show(
                new DecisionPromptDefinition(
                    new("Choose how to import", "インポート方法を選択"),
                    new("This item needs another package.", "このアイテムには別のパッケージが必要です。"),
                    new("Package not found", "パッケージが見つかりません"),
                    new("Import package", "パッケージをインポート"),
                    new("Continue without it", "なしで続行"),
                    new("Back", "戻る")),
                () => { selected = "primary"; return Task.CompletedTask; },
                () => { selected = "secondary"; return Task.CompletedTask; },
                () => { selected = "dismiss"; return Task.CompletedTask; });

            Check(state == (true, false), "Showing a prompt must show the native surface and leave waiting mode.");
            Check(labels["waiting"] == ("Choose how to import", "インポート方法を選択"), "The title must be localized from the supplied prompt.");
            Check(labels["placeholderMaterials"].English == "Continue without it", "The second choice must be replaceable.");
            prompt.InvokeSecondaryAsync().GetAwaiter().GetResult();
            Check(selected == "secondary", "The prompt must route the selected choice to its supplied action.");

            prompt.SetState(true, true);
            Check(state == (true, true), "A reusable prompt must preserve the waiting status while remaining visible.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
