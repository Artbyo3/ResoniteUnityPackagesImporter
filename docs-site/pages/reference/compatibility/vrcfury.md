# VRCFury

**Supported Armature Link operations, independent wardrobe toggles, and continuous blendshape sync transfer. Complex controllers and contacts do not.**

Recursive armature merges and single-bone attachments preserve supported bone targets, offset paths and transform options. Independent binary wardrobe toggles (`Toggle` with `BlendShapeAction`, `MaterialAction` supporting v1 `renderer` and v0 `obj`, and `AnimationClipAction`) are compiled with authored baseline restoration. Continuous live blendshape mirroring is driven by native `ValueCopy<float>` via `BlendShapeLink`. Arbitrary animator controllers (`FullController`), multi-layer state machines, contacts/haptics, and session persistence remain uncompiled.

VRCFury stores many features inside one serialized component. The support map lists those features separately so that Armature Link support cannot be mistaken for support for the whole package.
