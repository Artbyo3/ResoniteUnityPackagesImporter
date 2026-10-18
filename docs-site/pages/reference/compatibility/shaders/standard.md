# Standard and PBS materials

**Status: Supported.** Unity materials authored with Unity's standard Physically Based Shading (PBS) metallic workflow are converted directly to native Resonite `PBS_Metallic` materials.

## Supported property mappings

| Unity property | Resonite property | Description |
| --- | --- | --- |
| `_MainTex`, `_BaseMap`, `_MainTexture`, `_1st_ShadeMap` | `AlbedoTexture` | Main diffuse / base color map |
| Texture scale & offset | `TextureScale`, `TextureOffset` | Texture coordinates transform on UV0 |
| `_Color` | `AlbedoColor` | Base color tint factor |
| `_BumpMap` | `NormalMap` | Normal / bump map (imported with normal map flag) |
| `_BumpScale` | `NormalScale` | Normal map strength |
| `_MetallicGlossMap` | `MetallicMap` | Metallic / smoothness packed texture map |
| `_Metallic` | `Metallic` | Metallic value |
| `_Smoothness` | `Smoothness` | Surface smoothness value |
| `_EmissionMap` | `EmissiveMap` | Static emissive texture map |
| `_EmissionColor` | `EmissiveColor` | Emissive color tint factor |
| `_OcclusionMap` | `OcclusionMap` | Ambient occlusion map |

## Behavior and notes

- **Material assignments**: Assigned to the corresponding submesh slots on the imported mesh renderers.
- **Companion package recovery**: Missing texture inputs participate in companion package recovery if separated into another package.
- **Preset switching**: You can switch between Automatic conversion, explicit Xiexe Toon, or PBS Metallic from the station tablet preview.
