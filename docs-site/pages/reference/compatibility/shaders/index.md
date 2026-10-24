# Shaders and materials

**Material data is converted. Unity shader code does not run in Resonite.** Expect an approximation rather than a pixel-identical recreation of Unity or VRChat.

## Choose a conversion path

- **Automatic:** chooses a native conversion path from the source shader and its properties. Start here.
- **Toon:** explicitly selects the Xiexe Toon path.
- **PBS Metallic:** explicitly selects the native physically based material path.

Select an item on the station tablet and open its material customizer to change the preset. Inspect the preview before creating the avatar. Preset switching does not recreate unsupported effects.

## Supported shader families

- **Standard / PBS:** standard metallic property mappings, including base color, normals, metallic/smoothness, emission, and occlusion.
- **lilToon:** a supported subset, including compatible static first-layer emission maps and masks. Advanced and animated features remain limited.
- **Poiyomi:** supported mappings vary between the native toon and PBS paths. Toon-specific effects are not retained by the PBS path.

The shader-specific answers above list the actual mappings and limits. Other shader systems and custom Unity editor pipelines are outside the current compatibility target.

## Missing textures are a different problem

If the station asks for missing materials, supply the matching companion package. Changing the rendering preset cannot supply an asset that was not included.

Warnings appear on the item's status and its in-world receipt. Keep that context when reporting an unexpected result.
