# Your first import

## Before you start

Bring a `.unitypackage` with its required assets. Import into a private or local session if the content is private: the station's inactive staging hierarchy is still world content, not a local-only sandbox.

Keep the original files. Reconstruction can differ from Unity or VRChat, especially materials and build-time components.

## Import and review

1. **Drop your package into Resonite.** Choose **Import Unity Package**. The station prepares it for review; it does not immediately release a finished avatar.
2. **Supply missing dependencies if requested.** Choose **Import Companion Package** and drop the matching material package into the session. You can continue with placeholders instead, but the missing source data is not recovered that way.
3. **Select the avatar on the station tablet.** Rotate its preview, inspect the materials, and read its warnings before changing settings.
4. **Create it when the action is available.** Choose **Create avatar** and inspect the result in the world. Keep its import receipt: it records material assignments and limitations.

### What the status means

- **Ready:** structural checks passed and the assets were resolved.
- **Partial compatibility:** read the item details. A visual approximation alone does not block creation, but missing assets or structural problems can still prevent it.
- **Blocked:** resolve the reported structural problem before trying to create the avatar.

### What to expect visually

Unity shaders do not run in Resonite. Supported properties are converted to native materials. **Automatic** selects a conversion path; **Toon** and **PBS Metallic** let you override it. Switching presets cannot restore unsupported effects.
