import argparse
import asyncio
import json
import sys
from pathlib import Path
import websockets

sys.stdout.reconfigure(encoding='utf-8')

def load_japanese_messages():
    """Use the same station copy; unknown details keep their original wording."""
    folder = Path(__file__).resolve().parents[1] / "localization" / "station"
    if not (folder / "en.json").exists() or not (folder / "ja.json").exists():
        return {}
    en = json.loads((folder / "en.json").read_text(encoding="utf-8"))["messages"]
    ja = json.loads((folder / "ja.json").read_text(encoding="utf-8"))["messages"]
    return {value: ja.get(key, value) for key, value in en.items()}

async def run_demo(port: int):
    uri = f"ws://localhost:{port}/"
    print(f"Connecting to ResoniteLink at {uri}...")
    
    async with websockets.connect(uri, max_size=50*1024*1024) as ws:
        c = 0
        async def send(msg):
            nonlocal c; c += 1; mid = f"demo-{c}"
            msg["messageId"] = mid
            await ws.send(json.dumps(msg))
            while True:
                r = json.loads(await asyncio.wait_for(ws.recv(), timeout=15.0))
                if r.get("sourceMessageId") == mid:
                    if r.get("success") is False or r.get("error") or r.get("errorInfo"):
                        raise RuntimeError(f"ResoniteLink rejected {msg['$type']}: {r}")
                    return r

        # 1. Dynamically locate the Station Prototype under Root
        print("Discovering '[UnityPackage Station Prototype]'...")
        root_data = await send({"$type": "getSlot", "slotId": "Root", "depth": 2, "includeComponentData": False})
        station_id = None
        for ch in root_data.get("data", {}).get("children", []):
            if ch.get("name", {}).get("value", "") == "[UnityPackage Station Prototype]":
                station_id = ch.get("id")
                break

        if not station_id:
            print("ERROR: Could not find '[UnityPackage Station Prototype]' under Root.")
            raise RuntimeError("Station prototype is unavailable; no demo changes were made.")

        print(f"Found Station Prototype: {station_id}. Scanning components...")
        station_deep = await send({"$type": "getSlot", "slotId": station_id, "depth": 10, "includeComponentData": True})

        # Component IDs to discover
        pedestal_arc_comp = None
        pedestal_smooth_comp = None
        phase_text_comp = None
        file_text_comp = None
        data_count_comp = None
        data_total_comp = None
        has_completed_comp = None
        has_failed_comp = None
        msg_driver_comp = None
        canvas_scale_driver = None
        canvas_pos_driver = None
        canvas_copy_comp = None
        native_animation_slot = None
        component_data = {}
        localized_strings = {}
        japanese_messages = load_japanese_messages()

        def walk(node):
            nonlocal pedestal_arc_comp, pedestal_smooth_comp, phase_text_comp, file_text_comp
            nonlocal data_count_comp, data_total_comp, has_completed_comp, has_failed_comp
            nonlocal msg_driver_comp, canvas_scale_driver, canvas_pos_driver, canvas_copy_comp
            nonlocal native_animation_slot

            if not isinstance(node, dict): return
            sname = node.get("name", {}).get("value", "")
            for comp in (node.get("components") or []):
                component_data[comp["id"]] = comp
                if sname == "UI Localization" and comp.get("componentType", "").endswith("BooleanValueDriver<string>"):
                    target = comp.get("members", {}).get("TargetField", {}).get("targetId")
                    if target:
                        if target in localized_strings:
                            raise RuntimeError("Duplicate localization drivers target the same field.")
                        localized_strings[target] = comp["id"]
            if sname == "Native Import Animation":
                native_animation_slot = node.get("id")

            # Rainbow Stencil Arc & SmoothValue
            if sname == "Rainbow Stencil Writer Slot":
                for comp in (node.get("components") or []):
                    ctype = comp.get("componentType", "")
                    if "OutlinedArc" in ctype:
                        pedestal_arc_comp = comp.get("id")
                    elif "SmoothValue<float>" in ctype:
                        pedestal_smooth_comp = comp.get("id")

            # Disclosure Text Slots
            if sname == "Server Crasher":
                for comp in (node.get("components") or []):
                    if "Text" in comp.get("componentType", ""):
                        phase_text_comp = comp.get("id")

            if sname == "???":
                for comp in (node.get("components") or []):
                    if "Text" in comp.get("componentType", ""):
                        file_text_comp = comp.get("id")

            # Data Count & Total
            if sname == "Data":
                int_fields = [comp.get("id") for comp in (node.get("components") or []) if "ValueField<int>" in comp.get("componentType", "")]
                if len(int_fields) >= 2:
                    data_count_comp, data_total_comp = int_fields[0], int_fields[1]

            # Success / Fail flags
            if sname == "Border":
                bool_fields = [comp.get("id") for comp in (node.get("components") or []) if "ValueField<bool>" in comp.get("componentType", "")]
                if len(bool_fields) >= 2:
                    has_completed_comp, has_failed_comp = bool_fields[0], bool_fields[1]

            # Final Message Driver
            if sname == "Final Message":
                for comp in (node.get("components") or []):
                    if "BooleanValueDriver<string>" in comp.get("componentType", ""):
                        msg_driver_comp = comp.get("id")

            # Canvas Animation Drivers
            if sname == "Canvas":
                for comp in (node.get("components") or []):
                    ctype = comp.get("componentType", "")
                    cid = comp.get("id")
                    if "BooleanValueDriver<float3>" in ctype:
                        false_val = comp.get("members", {}).get("FalseValue", {}).get("value", {})
                        if false_val.get("y") == -1:
                            canvas_pos_driver = cid
                        else:
                            canvas_scale_driver = cid
                    elif "ValueCopy<bool>" in ctype and not canvas_copy_comp:
                        canvas_copy_comp = cid

            for child in (node.get("children") or []):
                if child: walk(child)

        walk(station_deep.get("data", {}))

        print(f"Mapped Components:")
        print(f" - Pedestal Arc:    {pedestal_arc_comp}")
        print(f" - Pedestal Smooth: {pedestal_smooth_comp}")
        print(f" - Phase Text:      {phase_text_comp}")
        print(f" - File Text:       {file_text_comp}")
        print(f" - Data Fields:     {data_count_comp}, {data_total_comp}")
        print(f" - Status Flags:    {has_completed_comp}, {has_failed_comp}")
        print(f" - Msg Driver:      {msg_driver_comp}")
        print(f" - Canvas Scale:    {canvas_scale_driver}")
        print(f" - Canvas Pos:      {canvas_pos_driver}")

        if not all([pedestal_arc_comp, pedestal_smooth_comp, phase_text_comp, file_text_comp, data_count_comp, data_total_comp, has_completed_comp, has_failed_comp, msg_driver_comp, canvas_scale_driver, canvas_pos_driver, canvas_copy_comp]):
            raise RuntimeError("Required lifecycle components are missing; no demo changes were made.")

        # The demo owns visibility. Disabled ValueCopy still holds its drive,
        # so release its Target as well. Keep its Source for later integration.
        if canvas_copy_comp:
            await send({
                "$type": "updateComponent",
                "entityId": canvas_copy_comp,
                "data": {"id": canvas_copy_comp, "members": {
                    "Enabled": {"$type": "bool", "value": False},
                    "Target": {"$type": "reference", "targetId": None}
                }}
            })

        async def set_pedestal_arc(deg, speed=None):
            members = {"TargetValue": {"$type": "float", "value": float(deg)}}
            if speed is not None:
                members["Speed"] = {"$type": "float", "value": float(speed)}
            await send({
                "$type": "updateComponent",
                "entityId": pedestal_smooth_comp,
                "data": {"id": pedestal_smooth_comp, "members": members}
            })

        async def set_localized_string(component_id, member, value):
            field_id = component_data[component_id]["members"][member]["id"]
            driver = localized_strings.get(field_id)
            # Localization owns the visible field. Update both source values,
            # so language switching continues to work during a demo stage.
            members = ({"FalseValue": {"$type": "string", "value": value},
                        "TrueValue": {"$type": "string", "value": japanese_messages.get(value, value)}}
                       if driver else {member: {"$type": "string", "value": value}})
            target = driver or component_id
            await send({"$type": "updateComponent", "entityId": target,
                        "data": {"id": target, "members": members}})

        async def set_hud(phase, file_name, count, total=120):
            if phase_text_comp:
                await set_localized_string(phase_text_comp, "Content", phase)
            if file_text_comp:
                await send({
                    "$type": "updateComponent",
                    "entityId": file_text_comp,
                    "data": {"id": file_text_comp, "members": {"Content": {"$type": "string", "value": file_name}}}
                })
            if data_count_comp:
                await send({
                    "$type": "updateComponent",
                    "entityId": data_count_comp,
                    "data": {"id": data_count_comp, "members": {"Value": {"$type": "int", "value": int(count)}}}
                })
            if data_total_comp:
                await send({
                    "$type": "updateComponent",
                    "entityId": data_total_comp,
                    "data": {"id": data_total_comp, "members": {"Value": {"$type": "int", "value": int(total)}}}
                })

        async def set_outcome(success=None, fail=None, msg_text=""):
            if success is not None and has_completed_comp:
                await send({
                    "$type": "updateComponent",
                    "entityId": has_completed_comp,
                    "data": {"id": has_completed_comp, "members": {"Value": {"$type": "bool", "value": success}}}
                })
            if fail is not None and has_failed_comp:
                await send({
                    "$type": "updateComponent",
                    "entityId": has_failed_comp,
                    "data": {"id": has_failed_comp, "members": {"Value": {"$type": "bool", "value": fail}}}
                })
            if msg_text and msg_driver_comp:
                member_key = "TrueValue" if fail else "FalseValue"
                await set_localized_string(msg_driver_comp, member_key, msg_text)

        async def set_card_visible(visible):
            if canvas_scale_driver:
                await send({
                    "$type": "updateComponent",
                    "entityId": canvas_scale_driver,
                    "data": {"id": canvas_scale_driver, "members": {"State": {"$type": "bool", "value": visible}}}
                })
            # The existing position ValueCopy follows the scale driver's State.

        async def dismiss_card():
            # Speed 10 is responsive during loading. Speed 3 was measured live
            # to reduce a full ring to ~0.02 degrees after two seconds.
            await set_pedestal_arc(0.0, speed=3.0)
            await set_card_visible(False)
            await asyncio.sleep(2.0)
            await set_pedestal_arc(0.0, speed=10.0)
            await asyncio.sleep(0.5)

        print("\n=======================================================")
        print(f">>> RUNNING STATION LIFECYCLE DEMO (PORT {port})")
        print("=======================================================\n")

        # Step 0: Initial state
        await set_outcome(success=False, fail=False)
        await set_card_visible(True)
        await set_pedestal_arc(0, speed=10.0)
        await set_hud("Initializing Import Pipeline...", "package_manifest.json", 0, 120)
        await asyncio.sleep(2.0)

        # ------------------------------------------------------------------
        # STAGE 1: PROGRESS TO SUCCESS (0% -> 100%)
        # ------------------------------------------------------------------
        print(">>> 1. Loading Assets (0% -> 100%)...")
        # Deliberately chunky targets demonstrate native frame-by-frame smoothing.
        for t in (0.0, 0.05, 0.12, 0.25, 0.38, 0.55, 0.72, 0.88, 1.0):
            arc_deg = t * 360.0
            count = int(t * 120)

            if t < 0.25:
                phase = "Extracting UnityPackage Archive"
                file_name = "Textures/Avatar_Texture_Albedo.png"
            elif t < 0.50:
                phase = "Decompressing Textures & Normal Maps"
                file_name = "Textures/Clothing_Normal.png"
            elif t < 0.75:
                phase = "Translating Shaders & Materials"
                file_name = "Materials/Body_Skin.mat"
            else:
                phase = "Finalizing Mesh & Rig Hierarchy"
                file_name = "Prefabs/Avatar_Root.prefab"

            await set_pedestal_arc(arc_deg)
            await set_hud(phase, file_name, count, 120)
            await asyncio.sleep(0.4)

        # ------------------------------------------------------------------
        # SUCCESS STATE: TURNS GREEN (CARD & PEDESTAL CELEBRATE)
        # ------------------------------------------------------------------
        print(">>> 2. SUCCESS! Turning GREEN with Completion Banner & Full 360 Ring...")
        await set_pedestal_arc(360.0)
        await set_outcome(success=True, fail=False, msg_text="Import Completed Successfully")
        # Hold full 360 ring and completion banner so the user clearly sees success
        await asyncio.sleep(3.5)

        # ------------------------------------------------------------------
        # SMOOTH EXIT: CARD SLIDES DOWN & PEDESTAL RING DISSOLVES TO 0 (CLEAN SHOWROOM)
        # ------------------------------------------------------------------
        print(">>> 3. Smooth Exit: Card slides away, Pedestal ring smoothly dissolves to 0...")
        # Trigger card disappear and smooth ring decay simultaneously
        await dismiss_card()

        # ------------------------------------------------------------------
        # STAGE 2: LOADING TO ERROR (0% -> 50%)
        # ------------------------------------------------------------------
        print(">>> 4. Starting Second Run: Loading towards Failure...")
        await set_outcome(success=False, fail=False)
        await set_hud("Re-importing Package...", "package_manifest.json", 0, 120)
        await set_card_visible(True)
        await asyncio.sleep(1.0)

        steps_fail = 18
        for i in range(steps_fail + 1):
            t = 0.5 * i / steps_fail
            arc_deg = t * 360.0
            count = int(t * 120)

            if t < 0.25:
                phase = "Decompressing Shaders"
                file_name = "Shaders/Custom_Toon_Opaque.shader"
            else:
                phase = "Compiling Custom Shader Bytecode"
                file_name = "Shaders/Unsupported_Geometry_Pass.shader"

            await set_pedestal_arc(arc_deg)
            await set_hud(phase, file_name, count, 120)
            await asyncio.sleep(0.12)

        # ------------------------------------------------------------------
        # FAILURE STATE: TURNS RED (CARD & PEDESTAL HALTED)
        # ------------------------------------------------------------------
        print(">>> 5. FAILURE! Turning RED with Error Banner...")
        await set_outcome(success=False, fail=True, msg_text="Import Failed: Unsupported Shader Bytecode")
        # Demo-only delay before simulating a user dismissal. A real failed
        # import must remain visible until the user dismisses it.
        await asyncio.sleep(4.0)

        # ------------------------------------------------------------------
        # ERROR DISMISS: CARD SLIDES AWAY & PEDESTAL RING DISSOLVES TO 0
        # ------------------------------------------------------------------
        print(">>> 6. Error Dismiss: Card slides away, Pedestal ring smoothly dissolves to 0...")
        await dismiss_card()

        print(">>> 7. Resetting to Standard Idle Preview...")
        await set_outcome(success=False, fail=False)
        await set_hud("Ready to import", "", 0, 120)
        await set_pedestal_arc(0.0, speed=10.0)
        await set_card_visible(False)

        # An accepted write can still be overridden by another field drive.
        # Confirm the actual final state before reporting success.
        for component_id, member, expected in (
            (canvas_scale_driver, "State", False),
            (canvas_pos_driver, "State", False),
            (has_completed_comp, "Value", False),
            (has_failed_comp, "Value", False),
            (pedestal_smooth_comp, "TargetValue", 0.0),
        ):
            state = await send({"$type": "getComponent", "componentId": component_id})
            actual = state["data"]["members"][member]["value"]
            if actual != expected:
                raise RuntimeError(f"Idle verification failed: {component_id}.{member} = {actual}")
        arc_state = await send({"$type": "getComponent", "componentId": pedestal_arc_comp})
        if abs(arc_state["data"]["members"]["Arc"]["value"]) > 0.1:
            raise RuntimeError("Idle verification failed: pedestal loading arc is still visible.")
        if native_animation_slot:
            animation = await send({"$type": "getSlot", "slotId": native_animation_slot,
                                    "depth": 0, "includeComponentData": False})
            if animation["data"]["isActive"]["value"]:
                raise RuntimeError("Idle verification failed: native loading animation is still active.")
        print(">>> Verified clean idle: hidden card, cleared ring and status flags.")

        print("\n>>> DEMO FINISHED SUCCESSFULLY!\n")

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="Run Station Prototype Demo Cycle")
    parser.add_argument("--port", type=int, default=19834, help="ResoniteLink port (default: 19834)")
    args = parser.parse_args()

    asyncio.run(run_demo(args.port))
