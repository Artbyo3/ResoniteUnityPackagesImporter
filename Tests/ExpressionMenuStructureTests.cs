using UnityPackageImporter.Models;

internal static class ExpressionMenuStructureTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        string Menu(string controls) => "MonoBehaviour:\n  m_Name: 'Authored: Menu #1'\n  controls:\n" + controls;
        VrcMenu Parse(string yaml) => ExpressionMenuParser.ParseMenuYaml(yaml, "Fallback.asset", "fixture");
        void Reject(string yaml)
        {
            try { Parse(yaml); }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected explicit malformed-menu rejection.");
        }
        test("Expressions preserve authored menu names and flow controls", () =>
        {
            var menu = Parse(Menu("  - {name: 'Coat: #1', type: 102, parameter: {name: 'Outfit: Coat'}, value: 2}\n"));
            var control = menu.Controls.Single();
            check(menu.Name == "Authored: Menu #1" && control.Name == "Coat: #1" && control.ParameterName == "Outfit: Coat" && control.Value == 2,
                "Flow mapping or authored exact names were lost.");
        });
        test("Expressions distinguish puppet parameters labels and controls", () =>
        {
            var menu = Parse(Menu("  - name: Face\n    type: 201\n    parameter: {name: ''}\n    subParameters:\n    - {name: Face/X}\n    - {name: Face/Y}\n    labels:\n    - {name: Up}\n    - {name: Down}\n    unrelated:\n      name: NotAParameter\n      type: 102\n      value: 99\n"));
            var control = menu.Controls.Single();
            check(control.Type == 201 && control.Value == 1 && control.ParameterName == "" &&
                control.SubParameters.SequenceEqual(new[] { "Face/X", "Face/Y" }) && control.Labels.SequenceEqual(new[] { "Up", "Down" }),
                "Nested names/types leaked into the control or primary parameter.");
        });
        foreach (int type in new[] { 101, 102, 103, 201, 202, 203, 987 })
            test("Expressions retain numeric control type " + type, () =>
            {
                var control = Parse(Menu("  - {name: Control, type: " + type + "}\n")).Controls.Single();
                check(control.Type == type && control.IsButton == (type == 101) && control.IsToggle == (type == 102) &&
                    control.IsSubMenu == (type == 103), "Unknown or puppet type became a supported toggle.");
            });
        test("Expressions ignore names outside the controls sequence", () =>
        {
            var menu = Parse(Menu("  - {name: Real, type: 102, parameter: {name: real}}\n") + "  metadata:\n  - name: Fake\n    type: 103\n");
            check(menu.Controls.Count == 1 && menu.Controls[0].ParameterName == "real", "Non-controls were parsed as controls.");
        });
        test("Expressions preserve duplicate display labels and exact parameter case", () =>
        {
            var controls = Parse(Menu("  - {name: Same, type: 102, parameter: {name: outfit}}\n  - {name: Same, type: 102, parameter: {name: Outfit}}\n")).Controls;
            check(controls.Count == 2 && controls[0].ParameterName != controls[1].ParameterName, "Display labels were treated as identities.");
        });
        test("Expressions parse empty authored menus", () =>
            check(Parse("MonoBehaviour: {m_Name: Empty, controls: []}").Controls.Count == 0, "Empty menu rejected."));
        test("Expressions retain exact external submenu and icon references", () =>
        {
            var control = Parse(Menu("  - {name: Nested, type: 103, subMenu: {fileID: 11400000, guid: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA}, icon: {fileID: 2800000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb}}\n")).Controls.Single();
            check(control.SubMenuGuid == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" && control.IconGuid == "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "References were guessed or normalized.");
        });
        test("Expressions allow null Unity asset references", () =>
        {
            var control = Parse(Menu("  - {name: Empty, type: 103, subMenu: {fileID: 0}, icon: {fileID: 0}}\n")).Controls.Single();
            check(control.SubMenuGuid == null && control.IconGuid == null, "Null Unity reference was invented.");
        });
        foreach (string malformed in new[]
        {
            "  - {name: UnknownType}\n", "  - {name: Bad, type: toggle}\n", "  - {name: Bad, type: 102, value: .nan}\n",
            "  - {name: Bad, type: 102, value: .inf}\n", "  - {name: Bad, type: 102, parameter: Wrong}\n",
            "  - {name: Bad, type: 102, parameter: {name: [nested]}}\n", "  - {name: [Bad], type: 102}\n",
            "  - {name: Bad, type: 203, subParameters: {name: Wrong}}\n", "  - {name: Bad, type: 203, subParameters: [Wrong]}\n",
            "  - {name: Bad, type: 203, labels: [Wrong]}\n", "  - {name: Bad, type: 103, subMenu: {fileID: 1, guid: wrong}}\n",
            "  - {name: Bad, type: 103, subMenu: {fileID: 0, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa}}\n",
            "  - {name: Bad, type: 103, icon: Wrong}\n", "  - {name: Bad, type: 102, type: 103}\n"
        })
            test("Expressions reject malformed control " + malformed.Trim(), () => Reject(Menu(malformed)));
        test("Expressions reject ambiguous serialized menu assets", () => Reject(Menu("  - {name: Real, type: 102}\n") + "---\nMonoBehaviour: {controls: []}\n"));
        test("Expressions propagate menu parsing cancellation", () =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { ExpressionMenuParser.ParseMenuYaml(Menu("  - {name: Real, type: 102}\n"), "a", "b", cancelled.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Cancellation was swallowed.");
        });
    }
}
