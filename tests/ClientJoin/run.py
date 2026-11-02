"""Run a local ENet server/client and require the complete player-ready handshake.

Build the Debug solution first, then run: python3 tests/ClientJoin/run.py /path/to/godot
Requires a Godot 4.7 .NET engine. No platform credentials or backend are needed.
"""

import argparse
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import threading


class LossProxy:
    """Interrupt client-to-server UDP once, while ENet and application ACKs are active."""

    def __init__(self, server_port, duration):
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.socket.bind(("127.0.0.1", 0))
        self.socket.settimeout(0.1)
        self.port = str(self.socket.getsockname()[1])
        self.server = ("127.0.0.1", int(server_port))
        self.duration = duration
        self.client = None
        self.upstream_packets = 0
        self.drop_started = None
        self.stopped = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        while not self.stopped.is_set():
            try:
                data, origin = self.socket.recvfrom(65535)
            except socket.timeout:
                continue
            if origin == self.server:
                if self.client:
                    self.socket.sendto(data, self.client)
                continue
            self.client = origin
            self.upstream_packets += 1
            if self.upstream_packets >= 100 and self.drop_started is None:
                self.drop_started = time.monotonic()
            if self.drop_started is not None and time.monotonic() - self.drop_started < self.duration:
                continue
            self.socket.sendto(data, self.server)

    def close(self):
        self.stopped.set()
        self.thread.join(timeout=1)
        self.socket.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("engine", nargs="?", default=shutil.which("godot"))
    parser.add_argument("world", nargs="?")
    parser.add_argument("--parts", type=int, default=0)
    parser.add_argument("--loss-window", type=float, default=0,
                        help="Drop upstream UDP for this many seconds during snapshot transfer.")
    options = parser.parse_args()
    engine = options.engine
    if not engine:
        raise SystemExit("Pass the path to a Godot 4.7 .NET executable.")
    root = Path(__file__).resolve().parents[2]
    env = os.environ.copy()
    dotnet = shutil.which("dotnet")
    if dotnet:
        env.setdefault("DOTNET_ROOT", str(Path(dotnet).resolve().parent))
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as available:
        available.bind(("127.0.0.1", 0))
        port = str(available.getsockname()[1])
    log_dir = Path(tempfile.mkdtemp(prefix="brickverse-join-smoke-"))
    processes = []
    proxy = LossProxy(port, options.loss_window) if options.loss_window > 0 else None
    world = Path(options.world).resolve() if options.world else None
    if options.parts:
        world = log_dir / "large-world.xml"
        world.write_text('<game><Item class="Environment"><Properties>'
                         '<string name="Name">Environment</string></Properties>'
                         + ''.join(f'<Item class="Part"><Properties><string name="Name">Block{i}</string>'
                                   '<bool name="Anchored">true</bool></Properties></Item>'
                                   for i in range(options.parts)) + '</Item></game>')
    try:
        for role in ("server", "client"):
            log = open(log_dir / f"{role}.log", "w")
            args = [
                engine,
                "--headless",
                "--log-file",
                str(log_dir / f"{role}-engine.log"),
                "--path",
                str(root / "BrickVerse"),
                "--",
                "--network",
                role,
                "--port",
                proxy.port if proxy and role == "client" else port,
                "--id",
                "joinsmoke",
            ]
            if role == "server" and world:
                args.extend(["--world", str(world)])
            process = subprocess.Popen(
                args, stdout=log, stderr=subprocess.STDOUT, env=env
            )
            processes.append((process, log))
            if role == "server":
                deadline = time.monotonic() + 30
                while (
                    "BrickVerse Server Started"
                    not in (log_dir / "server.log").read_text()
                ):
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError("Local server did not start.")
                    time.sleep(0.1)

        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            client_log = (log_dir / "client.log").read_text()
            if "RPC arg count mismatch" in client_log:
                raise RuntimeError(
                    "Replication RPC arguments do not match the receiver."
                )
            if "Dispatch Client run" in client_log:
                if proxy:
                    server_log = (log_dir / "server.log").read_text()
                    if proxy.drop_started is None or "[JoinReplication] Retrying" not in server_log:
                        raise RuntimeError("Join passed, but did not exercise snapshot retry recovery.")
                print(
                    "PASS: client authenticated, replicated the world, and completed player setup."
                )
                print(f"Logs: {log_dir}")
                return
            if any(process.poll() is not None for process, _ in processes):
                raise RuntimeError("A process exited before the client became ready.")
            time.sleep(0.1)
        raise RuntimeError(
            "Client did not complete the join handshake within 45 seconds."
        )
    finally:
        if proxy:
            proxy.close()
        for process, log in reversed(processes):
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            log.close()
        print(f"Join logs: {log_dir}")


if __name__ == "__main__":
    main()
