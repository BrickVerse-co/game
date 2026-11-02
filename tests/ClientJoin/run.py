"""Run a local ENet server/client and require the complete player-ready handshake.

Build the Debug solution first, then run: python3 tests/ClientJoin/run.py /path/to/godot
Requires a Godot 4.7 .NET engine. No platform credentials or backend are needed.
"""

import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time


def main():
    engine = sys.argv[1] if len(sys.argv) > 1 else shutil.which("godot")
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
    try:
        for role in ("server", "client"):
            log = open(log_dir / f"{role}.log", "w")
            args = [
                engine,
                "--headless",
                "--path",
                str(root / "BrickVerse"),
                "--",
                "--network",
                role,
                "--port",
                port,
                "--id",
                "joinsmoke",
            ]
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
