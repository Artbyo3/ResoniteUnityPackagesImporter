# lilToon

A supported subset of lilToon properties is converted to native Resonite materials. **This is an approximation, not a complete recreation of the Unity shader.**

## Static first-layer emission

Compatible emission maps and blend masks use the same texture-composition baker as Poiyomi. The conversion preserves lilToon's RGB-and-alpha mask multiplication and includes the main texture transform in the mask coordinates.

Inputs must share a representable UV set and transform. Missing compound inputs can recover across separate companion imports. If required material data is missing, the station may request a companion package.

## What is not handled by this path

- Animated coordinates, gradients, blinking and parallax.
- Non-additive blending and emission blended with the fully shaded base color.
- Incompatible wrapping and HDR input textures.
- Additional emission layers and other shader masks remain partial or unsupported.

Unsupported emission contributions are disabled with a warning rather than imported without their masks. Advanced and animated lilToon effects may also be missing.

## Where to check the result

Limitations appear on the item's unified import status, on the station tablet, and in its in-world receipt. Visual limitations alone do not block avatar creation; structural readiness is checked separately.
