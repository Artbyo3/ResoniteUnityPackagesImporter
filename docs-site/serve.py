"""Build and serve both English and Japanese documentation sites locally."""

import functools
import http.server
import os
import socketserver
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
    # Run build.py first
    import build

    build.main()

    site_dir = HERE / "site"
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(site_dir))
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
