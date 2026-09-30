using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.UI;

internal enum PackageImportChoice { Cancel, Import, Raw }

internal sealed class PreImportPrompt
{
    private readonly StationTemplate template;
    private readonly TaskCompletionSource<PackageImportChoice> choice = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Slot Host => template.Host;
    private PreImportPrompt(StationTemplate ui, IReadOnlyList<string> files)
    {
        template = ui;
        // The filename fitter owns the visible Text.Content. Update its full source
        // so the approved fitting behavior uses the actual package name.
        ui.Get<ValueField<string>>("filenameSource").Value.Value = files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + " packages";
        long bytes = files.Sum(file => new FileInfo(file).Length);
        ui.Get<Text>("size").Content.Value = (bytes / 1048576d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        Bind("import", PackageImportChoice.Import);
        Bind("raw", PackageImportChoice.Raw);
        Bind("cancel", PackageImportChoice.Cancel);
    }
    private void Bind(string key, PackageImportChoice result)
    {
        var button = template.Get<Button>(key);
        UnityStationHandle.ReleaseFixtureActions(button);
        NativeButtonEvents.Pressed(button, (_, _) => choice.TrySetResult(result));
    }
    public static async Task<PreImportPrompt> SpawnAsync(World world, float3 position, floatQ rotation, IReadOnlyList<string> files)
    {
        var template = await StationTemplateLoader.LoadAsync("pre-import", world, position, rotation);
        try { return new PreImportPrompt(template, files); }
        catch { if (!template.Host.IsDestroyed) template.Host.Destroy(); throw; }
    }
    public async Task<PackageImportChoice> WaitAsync()
    {
        while (!choice.Task.IsCompleted)
        {
            await Task.WhenAny(choice.Task, Task.Delay(250));
            await default(ToWorld);
            if (Host.IsDestroyed || Host.World.RootSlot.IsDestroyed) return PackageImportChoice.Cancel;
        }
        return await choice.Task;
    }
    public void Close() { if (!Host.IsDestroyed) Host.Destroy(); }
}
