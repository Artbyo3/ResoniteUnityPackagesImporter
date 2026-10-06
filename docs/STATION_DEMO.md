# Station demo

The reusable demo is already saved at `scripts/RunStationDemo.py`.
Run it from the repository root:

```powershell
python -u scripts/RunStationDemo.py
```

The script now finds the local port automatically using ResoniteLink's native
UDP session announcements. Discovery listens once for 12 seconds, then confirms
the endpoint with a read-only session metadata request before running the demo.
ResoniteLink must already be enabled in the intended world. Discovery does not
enable it, scan ports, or run in the background after the command ends.

If several local sessions announce Link, the script stops instead of guessing.
List the candidates and select the intended session by its exact name or ID:

```powershell
python scripts/FindResoniteLink.py --list
python -u scripts/RunStationDemo.py --session "Example world"
```

An explicitly supplied port always bypasses discovery:

```powershell
python -u scripts/RunStationDemo.py --port 49635
```

For other development tools, obtain just the verified port or a JSON endpoint:

```powershell
python scripts/FindResoniteLink.py
python scripts/FindResoniteLink.py --json
```

The helper accepts only announcements sent by this computer (loopback or a
local IPv4 address), replaces repeated announcements by stable session ID,
and removes native closure announcements. A shorter discovery window can miss
the engine's ten-second announcement interval; the default accommodates it.
Missing announcements can mean Link is disabled or networking is blocking
discovery. They do not prove that the game is closed. Enable Link and retry,
or use a known explicit port if UDP discovery is unavailable.

Protocol references: [official listener](https://github.com/Yellow-Dog-Man/ResoniteLink/blob/master/ResoniteLink/LinkSessionListener.cs)
and [official session model](https://github.com/Yellow-Dog-Man/ResoniteLink/blob/master/ResoniteLink/Models/ResoniteLinkSession.cs).
If terminal networking is sandboxed, use the normal escalation mechanism for
this command; do not modify the demo to work around the restriction.

## Expected cycle

1. Discover the exact `[UnityPackage Station Prototype]`, excluding the old
   reference copy, and map its existing components.
2. Demonstrate chunky progress through a native smooth pedestal arc.
3. Show the completed state for 3.5 seconds, then slide the card away and ease
   the ring to zero over approximately two seconds.
4. Demonstrate failure at 50%, hold the error for four seconds, then simulate
   dismissal. That timed dismissal is only part of the demo.
5. Verify clean idle: card hidden, ring cleared, status flags reset, native
   loading animation inactive. Print `DEMO FINISHED SUCCESSFULLY!`.

For a normal rerun, execute this command once, wait for the process to finish,
and report its actual result. No preliminary scan, script reread, component
rebuild, or extra audit is needed. Investigate only if the command fails.

The live setup and smoothing/visibility bindings are documented in
`scratch/station-prototype/pedestal-lifecycle-notes.md` if repair is needed.
Save the live station in Resonite to preserve its component changes across
sessions. This script and note are local files; running the demo does not
install or publish the mod.

The demo also supports the localized station. It discovers native
localization drivers by their target fields and updates their English and
Japanese source values instead of overwriting driven UI text. The wording
comes from `localization/station/en.json` and `ja.json`; filenames and unknown
error details keep their original wording. An older unlocalized station still
uses the original direct text updates. See `STATION_LOCALIZATION.md` for the
language policy and live verification.
