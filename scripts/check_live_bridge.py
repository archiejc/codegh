#!/usr/bin/env python3
"""Checks whether LiveCanvas MCP can reach the Rhino live bridge.

This performs a real MCP tools/call for `gh_session_info`.
It succeeds only when:
- stdio MCP host starts
- bridge websocket is reachable
- Rhino plugin returns session info
"""

from __future__ import annotations

import argparse
import json
import os
import queue
import subprocess
import sys
import threading
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Check LiveCanvas Rhino bridge readiness via MCP.")
    parser.add_argument(
        "--agent-host",
        required=True,
        help="Path to LiveCanvas.AgentHost executable, directory, or DLL.",
    )
    parser.add_argument(
        "--bridge-uri",
        default=None,
        help="Optional override for LIVECANVAS_BRIDGE_URI (for example ws://127.0.0.1:17881/livecanvas/v0).",
    )
    parser.add_argument(
        "--allow-offline",
        action="store_true",
        help="Return success even when Rhino bridge is offline.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=10.0,
        help="Timeout for process communication and response reads.",
    )
    return parser.parse_args()


def resolve_agent_host_path(raw: str) -> Path:
    p = Path(raw).expanduser().resolve()
    if p.is_dir():
        candidates = [
            p / "LiveCanvas.AgentHost",
            p / "LiveCanvas.AgentHost.exe",
            p / "LiveCanvas.AgentHost.dll",
        ]
        for candidate in candidates:
            if candidate.exists():
                return candidate
        raise FileNotFoundError(f"No LiveCanvas.AgentHost artifact found in directory: {p}")
    if not p.exists():
        raise FileNotFoundError(f"Agent host path does not exist: {p}")
    return p


def build_command(agent_host_path: Path) -> list[str]:
    if agent_host_path.suffix.lower() == ".dll":
        return ["dotnet", str(agent_host_path)]
    return [str(agent_host_path)]


def write_mcp_message(stdin: Any, payload: dict[str, Any]) -> None:
    body = json.dumps(payload).encode("utf-8")
    header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
    stdin.write(header + body)
    stdin.flush()


def _read_mcp_message_blocking(stdout: Any) -> dict[str, Any]:
    header = b""
    while b"\r\n\r\n" not in header:
        ch = stdout.read(1)
        if not ch:
            raise RuntimeError("MCP stdout closed before header was complete.")
        header += ch

    header_blob, remainder = header.split(b"\r\n\r\n", 1)
    content_length = None
    for line in header_blob.decode("ascii", errors="strict").split("\r\n"):
        if line.lower().startswith("content-length:"):
            content_length = int(line.split(":", 1)[1].strip())
            break
    if content_length is None:
        raise RuntimeError("MCP response missing Content-Length header.")

    body = remainder
    while len(body) < content_length:
        chunk = stdout.read(content_length - len(body))
        if not chunk:
            raise RuntimeError("MCP stdout closed before full body was read.")
        body += chunk
    return json.loads(body.decode("utf-8"))


def read_mcp_message(stdout: Any, timeout_seconds: float) -> dict[str, Any]:
    result_queue: queue.Queue[tuple[bool, Any]] = queue.Queue(maxsize=1)

    def worker() -> None:
        try:
            result_queue.put((True, _read_mcp_message_blocking(stdout)))
        except Exception as exc:  # pragma: no cover - exercised by script runtime
            result_queue.put((False, exc))

    threading.Thread(target=worker, daemon=True).start()

    try:
        ok, payload = result_queue.get(timeout=timeout_seconds)
    except queue.Empty as exc:
        raise TimeoutError(f"Timed out while waiting for an MCP response after {timeout_seconds:.1f}s.") from exc

    if ok:
        return payload

    raise payload


def main() -> int:
    args = parse_args()
    agent_host_path = resolve_agent_host_path(args.agent_host)
    command = build_command(agent_host_path)

    env = os.environ.copy()
    if args.bridge_uri:
        env["LIVECANVAS_BRIDGE_URI"] = args.bridge_uri

    process = subprocess.Popen(
        command,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        env=env,
    )

    try:
        if process.stdin is None or process.stdout is None:
            raise RuntimeError("Failed to open stdio pipes for AgentHost process.")

        write_mcp_message(
            process.stdin,
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "livecanvas-bridge-check", "version": "1.0"},
                },
            },
        )
        init_response = read_mcp_message(process.stdout, args.timeout_seconds)
        if init_response.get("error") is not None:
            raise RuntimeError(f"initialize failed: {init_response['error']}")

        write_mcp_message(
            process.stdin,
            {
                "jsonrpc": "2.0",
                "method": "notifications/initialized",
                "params": {},
            },
        )

        write_mcp_message(
            process.stdin,
            {
                "jsonrpc": "2.0",
                "id": 2,
                "method": "tools/call",
                "params": {"name": "gh_session_info", "arguments": {}},
            },
        )
        call_response = read_mcp_message(process.stdout, args.timeout_seconds)

        if call_response.get("error") is not None:
            err = call_response["error"]
            code = err.get("code")
            msg = err.get("message", "")
            if code == -32001:
                summary = {
                    "ok": False,
                    "bridgeAvailable": False,
                    "reason": msg,
                    "bridgeUri": env.get("LIVECANVAS_BRIDGE_URI", "ws://127.0.0.1:17881/livecanvas/v0"),
                }
                print(json.dumps(summary, indent=2))
                return 0 if args.allow_offline else 2
            raise RuntimeError(f"tools/call gh_session_info failed: {err}")

        structured = call_response.get("result", {}).get("structuredContent")
        if not isinstance(structured, dict):
            raise RuntimeError("tools/call gh_session_info did not return structuredContent.")

        summary = {
            "ok": True,
            "bridgeAvailable": True,
            "agent_host": str(agent_host_path),
            "bridgeUri": env.get("LIVECANVAS_BRIDGE_URI", "ws://127.0.0.1:17881/livecanvas/v0"),
            "sessionInfo": structured,
        }
        print(json.dumps(summary, indent=2))
        return 0
    except Exception as exc:
        print(f"[check_live_bridge] ERROR: {exc}", file=sys.stderr)
        return 1
    finally:
        process.terminate()
        try:
            _, stderr = process.communicate(timeout=args.timeout_seconds)
        except subprocess.TimeoutExpired:
            process.kill()
            _, stderr = process.communicate()
        if stderr:
            sys.stderr.write(stderr.decode("utf-8", errors="replace"))


if __name__ == "__main__":
    raise SystemExit(main())
