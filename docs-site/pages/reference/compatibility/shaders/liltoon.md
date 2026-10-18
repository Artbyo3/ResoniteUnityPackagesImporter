# lilToon

**Status: Partial.** The importer converts a supported subset of lilToon material properties to Resonite materials. The result is an approximation, not a complete recreation of the Unity shader.

Advanced and animated lilToon features may be missing. If the package does not include its material data, the importer may request a [companion package](../../../importing/companion-packages/).

Compatible static first-layer emission maps and blend masks use the same texture-composition baker as Poiyomi. lilToon's mask multiplies RGB and alpha; the conversion preserves that behavior and includes the main texture transform in mask coordinates. Missing compound inputs can recover across separate companion imports.

Inputs must share a representable UV set and transform. Animated coordinates, gradients, blinking, parallax, non-additive blending, emission blended with the fully shaded base color, incompatible wrapping and HDR input textures are not handled by this static path. Such emission contributions are disabled with a warning rather than imported without their mask. Limitations and unsupported shader features are reported on the item's unified import status, shown on the station tablet UI, and recorded in the in-world import receipt without blocking avatar creation. Additional emission layers and other shader masks remain partial or unsupported.
