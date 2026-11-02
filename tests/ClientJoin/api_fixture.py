"""Loopback HTTP fixture for the production client/server launch protocol."""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import threading
from urllib.parse import urlsplit


class ProductionApiFixture:
    def __init__(self, game_port, world, public_port=None):
        self.requests = []
        fixture = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_GET(self):
                self.respond()

            def do_POST(self):
                self.rfile.read(int(self.headers.get("Content-Length", "0")))
                self.respond()

            def do_DELETE(self):
                self.respond()

            def respond(self):
                path = urlsplit(self.path).path
                fixture.requests.append(path)
                content_type = "application/json"
                if path.endswith("/world/server/tree"):
                    body = world.read_bytes()
                    content_type = "application/octet-stream"
                elif path.endswith("/world/server/awaken"):
                    body = dict(
                        success=True,
                        worldId=1,
                        serverId="join-test-server",
                        port=int(game_port),
                        maxPlayers=20,
                    )
                elif path.endswith("/server/authorize-connection"):
                    body = dict(
                        status="STARTED",
                        ip="127.0.0.1",
                        port=int(public_port or game_port),
                        worldId=1,
                        serverId="join-test-server",
                        integrityAuthToken="test-integrity-authorization",
                    )
                elif path.endswith("/server/status"):
                    body = dict(status="STARTED")
                elif path.endswith("/world/server/user"):
                    body = dict(
                        id="12345",
                        username="ProductionJoinTest",
                        canChat=True,
                        isCreator=True,
                    )
                elif path == "/api/v3/world/1":
                    body = dict(
                        success=True,
                        world=dict(id="1", name="Join Test", maxPlayers=20),
                        universe=dict(
                            id="1",
                            name="Join Test",
                            creatorType="USER",
                            creatorId="12345",
                            creatorUser=dict(username="Join Test"),
                        ),
                    )
                elif "/profile/" in path:
                    body = dict(
                        success=True,
                        user=dict(
                            id="12345", username="ProductionJoinTest", statistics={}
                        ),
                    )
                elif path.endswith("/appearance"):
                    body = dict(success=False, appearance=dict(accessories=[]))
                else:
                    body = dict(success=True, remove=[], shouldShutdown=False)
                if not isinstance(body, bytes):
                    body = json.dumps(body).encode()
                self.send_response(200)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        # Debug builds use this documented development API address. Fail if an
        # existing development backend owns it; never replace that process.
        self.server = ThreadingHTTPServer(("127.0.0.1", 3001), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
