"""One-shot local discovery using ResoniteLink's native UDP announcements.

Protocol reference: Yellow-Dog-Man/ResoniteLink, LinkSessionListener.cs and
Models/ResoniteLinkSession.cs (verified against official library 0.13.1).
This helper never enables Link, scans ports, or changes the world.
"""

import argparse
import asyncio
from dataclasses import dataclass
import ipaddress
import json
import math
import socket
import sys
import time


ANNOUNCEMENT_PORT = 12512
DEFAULT_SECONDS = 12.0  # Native announcements are sent every ten seconds.


class DiscoveryError(RuntimeError):
    pass


@dataclass(frozen=True)
class LinkSession:
    session_id: str
    name: str
    port: int

    @property
    def url(self):
        # Resonite's local listener validates the Host header; using the
        # numeric loopback address can produce HTTP 400 on a valid Link port.
        return f"ws://localhost:{self.port}/"


def local_ipv4_addresses():
    addresses = {"127.0.0.1"}
    try:
        addresses.update(info[4][0] for info in socket.getaddrinfo(
            socket.gethostname(), None, socket.AF_INET, socket.SOCK_DGRAM))
    except OSError:
        pass
    return addresses


def decode_announcement(data, sender_ip, local_addresses):
    """Reject unrelated packets and LAN peers; preserve native close signals."""
    try:
        address = ipaddress.ip_address(sender_ip)
        if address.version != 4 or not (address.is_loopback or sender_ip in local_addresses):
            return None
        packet = json.loads(data.decode("utf-8-sig"))
        if not isinstance(packet, dict):
            return None
        session_id = packet.get("sessionID")
        name = packet.get("sessionName", "")
        port = packet.get("linkPort")
        if not isinstance(session_id, str) or not session_id.strip():
            return None
        if type(port) is not int:
            return None
        if port == 0 or port > 65535:
            return None
        # Native closure announcements require only the ID and negative port;
        # a missing/null name must not leave an old positive endpoint eligible.
        if port < 0 or name is None:
            name = ""
        if not isinstance(name, str):
            return None
        return LinkSession(session_id, name, port)
    except (ValueError, UnicodeError, RecursionError):
        return None


def record_announcement(sessions, announcement):
    if announcement is None:
        return
    if announcement.port < 0:
        sessions.pop(announcement.session_id, None)
    else:
        sessions[announcement.session_id] = announcement


def discover_sessions(seconds=DEFAULT_SECONDS):
    if not math.isfinite(seconds) or not 1 <= seconds <= 25:
        raise DiscoveryError("Discovery duration must be between 1 and 25 seconds.")
    sessions = {}
    addresses = local_ipv4_addresses()
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as listener:
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind(("0.0.0.0", ANNOUNCEMENT_PORT))
            deadline = time.monotonic() + seconds
            # Collect for the entire window: the first announcement may not be
            # the only open world. State exists only for this one invocation.
            while (remaining := deadline - time.monotonic()) > 0:
                listener.settimeout(remaining)
                try:
                    data, endpoint = listener.recvfrom(65535)
                except socket.timeout:
                    break
                record_announcement(sessions, decode_announcement(data, endpoint[0], addresses))
    except OSError as error:
        raise DiscoveryError("Could not listen for native ResoniteLink announcements: " + str(error)) from error
    return sorted(sessions.values(), key=lambda session: (session.name, session.session_id))


def select_session(sessions, selector=None):
    matches = [session for session in sessions if selector is None
               or selector in (session.session_id, session.name)]
    if not matches:
        raise DiscoveryError("No matching local ResoniteLink session was announced. "
                             "Enable ResoniteLink in the intended world and retry, "
                             "or provide --port explicitly.")
    if len(matches) != 1:
        choices = ", ".join(f"{session.name!r} (port {session.port})" for session in matches)
        raise DiscoveryError("Multiple local ResoniteLink sessions are available: " + choices
                             + ". Choose one with --session or --port.")
    return matches[0]


async def resolve_port(port=None, *, selector=None, seconds=DEFAULT_SECONDS):
    if port is not None:
        if type(port) is not int or not 1 <= port <= 65535:
            raise DiscoveryError("Port must be between 1 and 65535.")
        if selector is not None:
            raise DiscoveryError("Use --session with automatic discovery, or --port alone.")
        return port
    sessions = await asyncio.to_thread(discover_sessions, seconds)
    session = select_session(sessions, selector)
    await verify_session(session)
    return session.port


async def verify_session(session):
    """Read session metadata before any caller is allowed to change the world."""
    import websockets

    try:
        async with websockets.connect(session.url, open_timeout=3, close_timeout=1,
                                      max_size=1024 * 1024) as connection:
            await connection.send(json.dumps({"$type": "requestSessionData",
                                              "messageId": "discovery-check"}))
            async with asyncio.timeout(3):
                while True:
                    response = json.loads(await connection.recv())
                    if not isinstance(response, dict):
                        raise DiscoveryError("The endpoint returned invalid session metadata.")
                    if response.get("sourceMessageId") == "discovery-check":
                        if (response.get("$type") != "sessionData"
                                or response.get("success") is not True
                                or response.get("errorInfo")
                                or any(not isinstance(response.get(field), str) or not response[field]
                                       for field in ("resoniteVersion", "resoniteLinkVersion"))):
                            raise DiscoveryError("The endpoint did not confirm a valid ResoniteLink session.")
                        return
    except DiscoveryError:
        raise
    except (OSError, TimeoutError, ValueError, RecursionError, websockets.exceptions.WebSocketException) as error:
        raise DiscoveryError("The announced local ResoniteLink endpoint could not be verified. "
                             "Retry discovery after enabling Link in the intended world.") from error


def main():
    parser = argparse.ArgumentParser(description="Find a local ResoniteLink port using native announcements")
    parser.add_argument("--session", help="Exact session name or ID; needed when several worlds announce Link")
    parser.add_argument("--seconds", type=float, default=DEFAULT_SECONDS, help="Discovery window (default: 12 seconds)")
    parser.add_argument("--list", action="store_true", help="List announced local sessions without selecting one")
    parser.add_argument("--json", action="store_true", help="Return JSON instead of only the port")
    args = parser.parse_args()
    try:
        if args.list:
            sessions = discover_sessions(args.seconds)
            if not sessions:
                raise DiscoveryError("No local ResoniteLink session was announced.")
            print(json.dumps([{"sessionName": item.name, "sessionID": item.session_id,
                               "port": item.port, "url": item.url} for item in sessions], ensure_ascii=True))
        else:
            port = asyncio.run(resolve_port(selector=args.session, seconds=args.seconds))
            print(json.dumps({"port": port, "url": f"ws://localhost:{port}/"}) if args.json else port)
        return 0
    except DiscoveryError as error:
        print("ERROR: " + str(error), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Discovery cancelled.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
