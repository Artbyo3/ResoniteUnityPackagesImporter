using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;

internal static class ImportMeshReadiness
{
    internal static async Task WaitAsync(IEnumerable<SkinnedMeshRenderer> renderers, CancellationToken cancellationToken = default)
    {
        await default(ToWorld);
        var meshes = renderers.ToArray();
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (meshes.Any(mesh => mesh == null || mesh.IsDestroyed))
                throw new OperationCanceledException("Imported renderer was removed while loading.");
            if (meshes.Any(mesh => mesh.Mesh.Target == null))
                throw new InvalidOperationException("Imported renderer has no mesh asset.");
            if (meshes.All(mesh => mesh.Mesh.IsAssetAvailable)) return;
            if (timer.Elapsed >= TimeSpan.FromMinutes(2))
                throw new TimeoutException("Imported mesh assets did not become available within two minutes.");
            await default(NextUpdate);
        }
    }
}
