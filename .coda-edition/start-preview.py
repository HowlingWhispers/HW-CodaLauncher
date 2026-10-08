#!/usr/bin/env python3
"""Serve the isolated browser demo locally; does not run or install Minecraft."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import webbrowser

if __name__ == "__main__":
    directory = Path(__file__).resolve().parent / "web"
    server = ThreadingHTTPServer(("127.0.0.1", 0), partial(SimpleHTTPRequestHandler, directory=str(directory)))
    address = f"http://127.0.0.1:{server.server_port}/index.html"
    print(f"Coda Edition browser demo: {address}\nPress Ctrl+C to close.")
    webbrowser.open(address)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
