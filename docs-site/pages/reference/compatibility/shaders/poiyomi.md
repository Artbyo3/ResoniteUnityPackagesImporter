# Poiyomi

Poiyomi materials are translated to native Resonite materials. **The shader itself does not run in Resonite**, and the result is approximate.

## What transfers

- **Both paths:** base color and texture, normal maps, emission, blend mode, metallic texture channel selection and inversion.
- **Toon path:** UV selections, rim lighting, outlines, MatCap, occlusion, subsurface settings, and texture, multilayer, shade-map and skin shadow ramps.
- **PBS path:** detail textures and an approximation of height displacement. Toon-specific effects such as outlines and MatCap are lost on this path.

Automatic conversion selects Xiexe Toon for toon lighting and PBS Metallic for wrapped, realistic, cloth and SDF lighting, following the [official Unity SDK converter rules](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK/tree/809f86046a1d49ed44f30a80c89d67062fee5473/Assets/ResoniteSDK/MaterialConverters/Custom/Poiyomi). Disabling shading selects the toon path. The material editor's explicit toon override keeps Poiyomi property mappings while selecting Xiexe.

## What does not transfer

Animated shader effects, dissolve, AudioLink, additional emission layers, full color adjustment and arbitrary vertex manipulation are not reproduced. Texture and UV behavior is also limited by the target material's capabilities.

These visual limitations appear on the station tablet and the item's in-world import receipt without, by themselves, blocking avatar creation.

## Static emission and masks

Static emission maps and masks use a texture-composition baker. Masks preserve their selected RGBA channel and inversion. A mask without an emission map still restricts the glow; it is not replaced by solid white.

The supported static path requires:

- The same UV set and scale/offset for the inputs.
- Compatible repeat or clamp wrapping.
- Ordinary LDR textures. Sampling respects source color-space and alpha settings.
- For PBS emission, the albedo's UV0 transform as well.

Different UV domains or transforms, different wrapping modes, generated or animated mask coordinates, global-mask blending, base-color-dependent emission and HDR inputs require further work. Unsupported combinations produce a warning and disable that emission contribution; other material features remain available.

Missing composition inputs can be recovered through companion packages. Emission remains disabled until the required composition can be produced.

## Locked shaders and missing settings

Locked shaders are recognized when the shader name or retained material settings identify Poiyomi. Packages that strip those identifiers or required values cannot be recovered automatically. Missing textures still participate in companion-package recovery.

Packages usually omit Unity project color-space settings. Automatic/HDR colors assume a linear project, and missing shader defaults use documented converter fallbacks.

Packed metallic channels are rearranged as raw numeric data. Shadow-ramp tinting uses the SDK's linear-light calculation. Finite-value guards handle zero-width shadow transitions and invalid subsurface spread.
