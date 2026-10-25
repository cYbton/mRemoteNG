# Local helper for Export-StencilSvgs.ps1 - serves render.html and the draw.io viewer from the work
# folder and saves the SVGs the page sends back. Listens on 127.0.0.1 only and ends itself once the
# page reports it is done.
#
# Usage: python render-server.py <port> <work folder> <output folder>
import http.server
import os
import re
import sys
import threading

PORT, ROOT, OUT = int(sys.argv[1]), sys.argv[2], sys.argv[3]


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def log_message(self, format, *args):
        pass

    def do_PUT(self):
        # Only /save/<lib>/[<group>/]<name>.svg with plain names - nothing else is written
        match = re.fullmatch(r"/save/((?:[a-z0-9_]+/){1,2}[a-z0-9_]+)\.svg", self.path)
        if not match:
            self.send_error(400)
            return
        body = self.rfile.read(int(self.headers["Content-Length"]))
        target = os.path.join(OUT, *match.group(1).split("/")) + ".svg"
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "wb") as f:
            f.write(body)
        self.send_response(204)
        self.end_headers()

    def do_POST(self):
        if self.path != "/done":
            self.send_error(400)
            return
        body = self.rfile.read(int(self.headers["Content-Length"]))
        with open(os.path.join(OUT, "_summary.json"), "wb") as f:
            f.write(body)
        self.send_response(204)
        self.end_headers()
        threading.Thread(target=self.server.shutdown).start()


http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
