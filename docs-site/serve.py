"""Build and serve both English and Japanese documentation sites locally."""

import functools
import http.server
import os
import socketserver
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


class NoCacheHTTPHandler(http.server.SimpleHTTPRequestHandler):
    """Serve files with cache-busting headers so local changes reflect immediately."""

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
        self.send_header("Pragma", "no-cache")
        self.send_header("Expires", "0")
        super().end_headers()


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
    # Run build.py first
    import build

    build.main()

    site_dir = HERE / "site"
    handler = functools.partial(NoCacheHTTPHandler, directory=str(site_dir))
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("", port), handler) as httpd:
        print(f"\nServing documentation site locally on http://localhost:{port}")
        print(f"  English:  http://localhost:{port}/")
        print(f"  Japanese: http://localhost:{port}/ja/")
        print("Press Ctrl+C to stop.\n")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nDev server stopped.")


if __name__ == "__main__":
    main()
