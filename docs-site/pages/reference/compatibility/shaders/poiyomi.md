# Poiyomi

**Status: Experimental conversion in development builds.** Poiyomi materials are translated to native Resonite materials using rules adapted from the [official Unity SDK's Poiyomi converters](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK/tree/809f86046a1d49ed44f30a80c89d67062fee5473/Assets/ResoniteSDK/MaterialConverters/Custom/Poiyomi). The Poiyomi shader itself does not run in Resonite. Visual validation in Resonite is still required.

The automatic path chooses Xiexe Toon for toon lighting and PBS Metallic for wrapped, realistic, cloth and SDF lighting, following the SDK. Disabling shading selects the toon path. The material editor's explicit toon override keeps Poiyomi property mappings while selecting Xiexe.

Supported mappings include base color and texture, normal maps, emission, blend mode, metallic texture channel selection and inversion. The toon path also maps UV selections, rim lighting, outlines, MatCap, occlusion, subsurface settings, and texture, multilayer, shade-map and skin shadow ramps. The PBS path maps detail textures and approximates height displacement.

Static emission maps and masks now use a shared texture-composition baker. Poiyomi masks preserve their selected RGBA channel and inversion. A mask without an emission map still restricts the glow; it is not replaced by solid white. Missing inputs participate in companion-package recovery, and emission stays disabled until the required composition can be produced.

This path supports inputs with the same UV set and scale/offset, repeat or clamp wrapping, and ordinary LDR textures. It respects source texture color-space and alpha settings when sampling. Different UV domains/transforms, different wrapping modes, generated coordinates, animated mask coordinates, global-mask blending, base-color-dependent emission and HDR texture inputs require further work. Unsupported combinations produce a log warning and disable that emission contribution. PBS emission additionally requires the albedo's UV0 transform. Other material features remain available.

Conversion is approximate. The PBS path loses toon-specific effects such as outlines and MatCap. Animated shader effects, dissolve, AudioLink, additional emission layers, full color adjustment and arbitrary vertex manipulation are not reproduced. Detected limitations and approximated features are reported on the item's unified import status, displayed on the station tablet UI, and saved in the in-world import receipt without blocking avatar creation. Texture and UV behavior is limited to the target material's capabilities.

Locked shaders are recognized when their shader name or retained material settings identify Poiyomi. Optimized packages that strip those identifiers or required values cannot be recovered automatically. Package files usually do not include Unity project color-space settings; automatic/HDR colors assume a linear project, and missing shader defaults use documented converter fallbacks. Missing textures still participate in companion-package recovery.

Packed metallic channels are rearranged as raw numeric data. Shadow-ramp tinting uses the SDK's linear-light calculation. The conversion includes finite-value guards for zero-width shadow transitions and invalid subsurface spread.
