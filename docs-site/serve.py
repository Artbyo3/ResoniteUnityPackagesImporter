import http.server
import socketserver
import os
import sys

PORT = 8000
DIRECTORY = os.path.join(os.path.dirname(os.path.abspath(__file__)), "site")

class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)

if __name__ == "__main__":
    sys.stdout.reconfigure(line_buffering=True)
    with socketserver.TCPServer(("", PORT), Handler) as httpd:
        print(f"Docs server active on port {PORT}")
        httpd.serve_forever()
