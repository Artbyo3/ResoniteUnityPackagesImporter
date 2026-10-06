# Station demo shortcut

When the user asks to run the station demo, run the existing
`scripts/RunStationDemo.py` once. It automatically discovers and verifies the
local ResoniteLink endpoint using the engine's native session announcements.
If the user supplies a port, pass it explicitly with `--port` instead.

Do not recreate the demo, rescan the world, or inspect the mod just to rerun it.
Wait for completion and report the result. If discovery finds several sessions,
ask which session to use; do not choose the first. If discovery finds none,
ask the user to enable ResoniteLink in the intended world and retry. An explicit
port remains available when native announcements are unavailable. For details
or troubleshooting, read `docs/STATION_DEMO.md`.

This is a live prototype demo. Running it does not authorize mod integration,
installation, recurring automations, or GitHub publication.
