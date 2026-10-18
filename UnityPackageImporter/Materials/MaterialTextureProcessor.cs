using System;
using System.IO;
using Elements.Assets;
using Elements.Core;

namespace UnityPackageImporter.Materials;

/// <summary>Shared pixel-processing path. Decoding preserves source dimensions before any explicit resize policy.</summary>
internal static class MaterialTextureProcessor
{
    internal static void ProcessFile(string source, string destination, bool multiplyRgbByAlpha = false,
        TexturePixelTransform transform = null, Action checkCancellation = null)
    {
        checkCancellation?.Invoke();
        // maxSize and sizeRatio are resizing parameters, not sentinel flags. Their defaults retain the source size.
        var bitmap = Bitmap2D.Load(source, generateMipmaps: false, alphaHandling: AlphaHandling.ForceRGBA);
        if (bitmap.Size.x <= 0 || bitmap.Size.y <= 0)
            throw new InvalidDataException("Decoded material texture has invalid dimensions.");
        for (int y = 0; y < bitmap.Size.y; y++)
        {
            checkCancellation?.Invoke();
            for (int x = 0; x < bitmap.Size.x; x++)
            {
                var c = bitmap[x, y];
                if (multiplyRgbByAlpha) c = new color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                if (transform != null)
                {
                    var processed = transform.ApplyStored(c.r, c.g, c.b, c.a);
                    c = new color(processed.r, processed.g, processed.b, processed.a);
                }
                bitmap[x, y] = c;
            }
        }
        checkCancellation?.Invoke();
        if (!bitmap.Save(destination, preserveColorInAlpha: true))
            throw new IOException("Could not encode processed material texture.");
    }
}
