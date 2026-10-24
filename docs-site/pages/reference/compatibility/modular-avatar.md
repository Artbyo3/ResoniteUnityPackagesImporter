# Modular Avatar

**Outfit structure, independent wardrobe toggles, and continuous blendshape sync transfer. Complex controllers and session persistence do not.** A component being present in a package does not mean its full Unity build process runs in Resonite.

Merge Armature and Bone Proxy translate supported structural operations. Independent binary wardrobe controls (`ModularAvatarMenuItem` SubMenu and Toggle, `ModularAvatarMenuInstaller`, `ModularAvatarMenuGroup`, and `ModularAvatarParameters` with typed definitions) are compiled into native Resonite controls. Reactive visibility (`ModularAvatarObjectToggle`), blendshapes (`ModularAvatarShapeChanger` Set mode), and material slots (`ModularAvatarMaterialSetter` with `m_inverted`) restore authored baselines when switched off. Live continuous blendshape mirroring is driven by native `ValueCopy<float>` via `ModularAvatarBlendshapeSync`. Arbitrary multi-layer animator controllers, puppetry, destructive cuts, and session persistence remain uncompiled.

The support map below keeps each component's actual scope and remaining gap together. Open a row for the reason and implementation evidence, without leaving this guide.
