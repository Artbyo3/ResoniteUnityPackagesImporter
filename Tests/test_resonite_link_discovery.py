import asyncio
import json
from pathlib import Path
import socket
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import FindResoniteLink as discovery


def packet(session_id="one", name="Example world", port=49635):
    return json.dumps({"sessionID": session_id, "sessionName": name, "linkPort": port}).encode("utf-8")


class DiscoveryTests(unittest.TestCase):
    def decode(self, data, address="127.0.0.1"):
        return discovery.decode_announcement(data, address, {"192.168.1.10"})

    def test_native_unicode_packet_and_announced_port(self):
        session = self.decode(packet(name="テスト世界"))
        self.assertEqual(session.name, "テスト世界")
        self.assertEqual(session.url, "ws://localhost:49635/")
        self.assertIsNotNone(self.decode(packet(), "192.168.1.10"))

    def test_ignore_lan_peers_and_unrelated_malformed_packets(self):
        self.assertIsNone(self.decode(packet(), "192.168.1.11"))
        for data in (b"not json", b"\xff", b"null", b"[]", b"{}",
                     packet(session_id=""), packet(port=True), packet(port="49635"),
                     packet(port=0), packet(port=65536)):
            with self.subTest(data=data):
                self.assertIsNone(self.decode(data))

    def test_repeated_id_updates_name_and_port_without_duplicate_choice(self):
        sessions = {}
        discovery.record_announcement(sessions, self.decode(packet()))
        discovery.record_announcement(sessions, self.decode(packet(name="Renamed world", port=50000)))
        selected = discovery.select_session(list(sessions.values()))
        self.assertEqual(len(sessions), 1)
        self.assertEqual((selected.name, selected.port), ("Renamed world", 50000))

    def test_null_or_missing_name_does_not_block_native_closure(self):
        for closed in (packet(name=None, port=-1), b'{"sessionID":"one","linkPort":-1}'):
            with self.subTest(closed=closed):
                sessions = {}
                discovery.record_announcement(sessions, self.decode(packet()))
                discovery.record_announcement(sessions, self.decode(closed))
                discovery.record_announcement(sessions, self.decode(closed))
                self.assertEqual(sessions, {})

    def test_no_or_multiple_sessions_never_choose_arbitrarily(self):
        first = self.decode(packet())
        second = self.decode(packet(session_id="two", name="Other world", port=50000))
        for sessions in ([], [first, second]):
            with self.assertRaises(discovery.DiscoveryError):
                discovery.select_session(sessions)
        self.assertEqual(discovery.select_session([first, second], "Other world"), second)
        self.assertEqual(discovery.select_session([first, second], "two"), second)
        same_name = self.decode(packet(session_id="three", name=first.name))
        with self.assertRaises(discovery.DiscoveryError):
            discovery.select_session([first, same_name], first.name)

    def test_explicit_port_bypasses_discovery(self):
        with patch.object(discovery, "discover_sessions", side_effect=AssertionError("Should not discover")):
            self.assertEqual(asyncio.run(discovery.resolve_port(12036)), 12036)
        for port in (0, -1, 65536, True):
            with self.assertRaises(discovery.DiscoveryError):
                asyncio.run(discovery.resolve_port(port))
        with self.assertRaises(discovery.DiscoveryError):
            asyncio.run(discovery.resolve_port(12036, selector="Example world"))

    def test_discovery_collects_full_window_and_closes_its_socket(self):
        packets = [(packet(), ("127.0.0.1", 11111)),
                   (packet(session_id="two", port=50000), ("127.0.0.1", 22222)),
                   (packet(name=None, port=-1), ("127.0.0.1", 33333))]

        class Listener:
            closed = False
            timeouts = []
            def __enter__(self): return self
            def __exit__(self, *args): self.closed = True
            def setsockopt(self, *args): pass
            def bind(self, endpoint): self.endpoint = endpoint
            def settimeout(self, timeout): self.timeouts.append(timeout)
            def recvfrom(self, size):
                if packets: return packets.pop(0)
                raise socket.timeout()

        listener = Listener()
        with patch.object(discovery.socket, "socket", return_value=listener), \
                patch.object(discovery, "local_ipv4_addresses", return_value={"127.0.0.1"}), \
                patch.object(discovery.time, "monotonic", side_effect=[0, 1, 2, 3, 4]):
            sessions = discovery.discover_sessions(12)
        self.assertEqual([session.port for session in sessions], [50000])
        self.assertTrue(listener.closed)
        self.assertEqual(listener.endpoint, ("0.0.0.0", 12512))
        self.assertEqual(listener.timeouts, [11, 10, 9, 8])

    def test_invalid_discovery_window_is_rejected_before_listening(self):
        for seconds in (0, 26, float("nan"), float("inf")):
            with self.assertRaises(discovery.DiscoveryError):
                discovery.discover_sessions(seconds)


class SessionVerificationTests(unittest.IsolatedAsyncioTestCase):
    async def verify(self, response, unsolicited=None):
        class Connection:
            sent = []
            responses = iter(([unsolicited] if unsolicited else []) + [response])
            async def __aenter__(self): return self
            async def __aexit__(self, *args): pass
            async def send(self, message): self.sent.append(json.loads(message))
            async def recv(self): return json.dumps(next(self.responses))

        connection = Connection()
        with patch("websockets.connect", return_value=connection):
            await discovery.verify_session(discovery.LinkSession("advertised-id", "Example world", 49635))
        self.assertEqual(connection.sent, [{"$type": "requestSessionData", "messageId": "discovery-check"}])

    def metadata(self, **overrides):
        return {"$type": "sessionData", "sourceMessageId": "discovery-check", "success": True,
                "resoniteVersion": "2026.9.18.82", "resoniteLinkVersion": "0.13.1.0",
                "uniqueSessionId": "different-connection-id", **overrides}

    async def test_confirm_native_metadata_without_confusing_two_id_types(self):
        await self.verify(self.metadata(), {"$type": "other", "sourceMessageId": "unrelated"})

    async def test_correlated_non_session_response_cannot_authorize_demo(self):
        for response in ([1, 2], self.metadata(success=False), self.metadata(success=None),
                         self.metadata(**{"$type": "slotData"}), self.metadata(resoniteVersion=""),
                         self.metadata(resoniteLinkVersion=None), self.metadata(errorInfo="Rejected")):
            with self.subTest(response=response), self.assertRaises(discovery.DiscoveryError):
                await self.verify(response)


if __name__ == "__main__":
    unittest.main()
