#!/usr/bin/env python3
"""Benchmark and screenshot the real 1.2 client through its stdio MCP server.

Uses the already running local login/game servers. Never downloads or edits client assets.
The password is passed by filename, never read or printed by this script.
"""
import argparse
import base64
import json
import math
import pathlib
import re
import statistics
import subprocess
import time
import threading


class PriorityProbe:
    """Sample only this client's scheduling class; does not need administrator rights."""
    def __init__(self, pid):
        import ctypes
        from ctypes import wintypes
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        self.kernel.OpenProcess.restype = wintypes.HANDLE
        self.kernel.GetPriorityClass.argtypes = [wintypes.HANDLE]
        self.kernel.GetPriorityClass.restype = wintypes.DWORD
        self.kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        self.handle = self.kernel.OpenProcess(0x1000, False, pid)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())
        self.samples = []
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.sample)
        self.thread.start()

    def sample(self):
        started = time.monotonic()
        while not self.stop.is_set():
            self.samples.append([round(time.monotonic() - started, 4),
                                 self.kernel.GetPriorityClass(self.handle)])
            self.stop.wait(0.005)

    def finish(self):
        self.stop.set()
        self.thread.join()
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None
        return self.samples


class Rpc:
    def __init__(self, command):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        text=True, encoding="utf-8")
        self.sequence = 0
        self.call("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                                 "clientInfo": {"name": "aaemu-client-smoke", "version": "1"}})
        self.process.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n")
        self.process.stdin.flush()

    def call(self, method, params=None):
        self.sequence += 1
        request = {"jsonrpc": "2.0", "id": self.sequence, "method": method}
        if params is not None:
            request["params"] = params
        self.process.stdin.write(json.dumps(request) + "\n")
        self.process.stdin.flush()
        line = self.process.stdout.readline()
        if not line:
            raise RuntimeError("MCP server closed stdout")
        response = json.loads(line)
        if response.get("id") != self.sequence:
            raise RuntimeError("MCP response ID mismatch")
        if "error" in response:
            raise RuntimeError(str(response["error"]))
        return response["result"]

    def tool(self, tool_name, **arguments):
        result = self.call("tools/call", {"name": tool_name, "arguments": arguments})
        if result.get("isError"):
            raise RuntimeError("\n".join(c.get("text", "") for c in result["content"]))
        return result

    def text(self, tool_name, **arguments):
        return "\n".join(c["text"] for c in self.tool(tool_name, **arguments)["content"] if c["type"] == "text")

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=10)


def wait_stage(rpc, pid, expected, timeout):
    deadline = time.monotonic() + timeout
    last = "not ready"
    while time.monotonic() < deadline:
        try:
            # Stage alone is safe at server select; character-count APIs require a world connection.
            last = rpc.text("client_lua", pid=pid, target="ui",
                            code="return X2LoginCharacter and X2LoginCharacter:GetCurrentStage()")
            if last == str(expected):
                return
        except RuntimeError as error:
            last = str(error)
        time.sleep(0.5)
    raise RuntimeError(f"stage {expected} not reached within {timeout}s: {last}")


def shot(rpc, pid, output):
    content = rpc.tool("client_screenshot", pid=pid)["content"]
    image = next(c for c in content if c["type"] == "image")
    png = base64.b64decode(image["data"], validate=True)
    if not png.startswith(b"\x89PNG\r\n\x1a\n"):
        raise RuntimeError("MCP returned invalid PNG bytes")
    output.write_bytes(png)


def wait_player(rpc, pid, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        state = rpc.text("client_lua", pid=pid, target="ui", code="return X2LoginCharacter:GetCurrentStage(), X2Unit and X2Unit:UnitName('player')")
        # The lobby stage resets to 0 in-world; player state distinguishes this from boot.
        stage, _, name = state.partition("\t")
        if stage == "0" and name not in ("", "nil"):
            return
        time.sleep(0.5)
    raise RuntimeError("in-world player state did not become available")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--driver-exe", type=pathlib.Path, required=True)
    parser.add_argument("--dll", type=pathlib.Path, required=True)
    parser.add_argument("--client", type=pathlib.Path, required=True)
    parser.add_argument("--password-file", type=pathlib.Path, required=True)
    parser.add_argument("--login", default="127.0.0.1:1237")
    parser.add_argument("--user", default="clienttest")
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument("--trials", type=int, default=1)
    parser.add_argument("--compare", action="store_true", help="alternate normal and fast launches with identical window settings")
    parser.add_argument("--compare-smooth", action="store_true", help="alternate original and normal-priority clock calibration, sampling process priority")
    parser.add_argument("--hidden", action="store_true", help="hide both profiles while rendering")
    parser.add_argument("--enter-world", action="store_true", help="also enter world 1 with its selected test character")
    parser.add_argument("--set-cvar", action="append", default=[], metavar="NAME=VALUE",
                        help="set an engine CVar before world entry (repeatable, for loading experiments)")
    parser.add_argument("--pak-profile", action="store_true", help="record raw pak read timings and offsets during world entry")
    parser.add_argument("--fast-pak", action="store_true", help="use 64 KB pak read-ahead before entering the world")
    parser.add_argument("--compare-pak", action="store_true", help="alternate 1 MB and 64 KB pak read-ahead with fast startup in both")
    parser.add_argument("--verify-pak", action="store_true", help="compare engine read bytes, seeks and EOF under both pak profiles")
    parser.add_argument("--timeout", type=float, default=120)
    args = parser.parse_args()
    if args.trials < 1:
        parser.error("--trials must be positive")
    if args.compare_pak and (args.compare or not args.enter_world):
        parser.error("--compare-pak requires --enter-world and cannot be combined with --compare")
    if args.compare_smooth and (args.compare or args.compare_pak):
        parser.error("--compare-smooth cannot be combined with other comparisons")
    cvars = {}
    for setting in args.set_cvar:
        name, separator, value = setting.partition("=")
        if not separator or not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]*", name):
            parser.error("--set-cvar expects NAME=VALUE")
        try:
            cvars[name] = float(value)
        except ValueError:
            parser.error("--set-cvar values must be numeric")
        if not math.isfinite(cvars[name]):
            parser.error("--set-cvar values must be finite")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    command = [str(args.driver_exe), "mcp", "--dll", str(args.dll), "--client", str(args.client),
               "--password-file", str(args.password_file), "--login", args.login, "--user", args.user,
               "--artifact-dir", str(args.output_dir / "mcp")]
    rpc = Rpc(command)
    measurements = []
    try:
        available = rpc.call("tools/list")["tools"]
        assert any(t["name"] == "client_screenshot" for t in available)
        if json.loads(rpc.text("client_list")):
            raise RuntimeError("a client is already running; close it before benchmarking")
        for trial in range(args.trials):
            profiles = ([(True, args.fast_pak, "smooth-baseline"), (True, args.fast_pak, "smooth")] if args.compare_smooth else
                        [(True, False, "pak-baseline"), (True, True, "pak-64k")] if args.compare_pak else
                        [(fast, args.fast_pak, "fast" if fast else "normal")
                         for fast in ([False, True] if args.compare else [True])])
            for fast, fast_pak, profile in profiles:
                pid = None
                priority = None
                smooth = profile != "smooth-baseline"
                label = f"{profile}-{trial + 1}"
                started = time.monotonic()
                try:
                    launch = rpc.text("client_launch", fast_start=fast, fast_pak=fast_pak, smooth_start=smooth, hidden=args.hidden)
                    pid = int(re.search(r"client started \(pid (\d+)\)", launch)[1])
                    if args.compare_smooth:
                        priority = PriorityProbe(pid)
                    wait_stage(rpc, pid, 3, args.timeout)
                    seconds = round(time.monotonic() - started, 3)
                    status = rpc.text("client_status", pid=pid)
                    if fast and "fast=1" not in status:
                        raise RuntimeError("fast-start CVar was not applied")
                    if f"smooth={int(smooth)}" not in status:
                        raise RuntimeError("CPU calibration priority profile was not applied")
                    priority_samples = priority.finish() if priority else None
                    # Readiness is timed above; wait for the server-list fade before its image.
                    time.sleep(1.5)
                    shot(rpc, pid, args.output_dir / f"{label}.png")
                    rpc.text("client_lua", pid=pid, target="system", code="return 1+2*3")
                    # Lua failure must arrive as an MCP tool error, with the server still usable.
                    failure = rpc.call("tools/call", {"name": "client_lua", "arguments": {"pid": pid, "code": "error('smoke expected error')"}})
                    assert failure.get("isError") is True
                    rpc.text("client_status", pid=pid)
                    observer = Rpc(command)
                    try:
                        refusal = observer.call("tools/call", {"name": "client_stop", "arguments": {"pid": pid}})
                        assert refusal.get("isError") is True
                    finally:
                        observer.close()
                    if args.enter_world:
                        pak_state = json.loads(rpc.text("client_pak", pid=pid, action="status"))
                        if pak_state["optimized"] != fast_pak:
                            raise RuntimeError("pak read-ahead profile was not applied")
                        for name, value in cvars.items():
                            actual = rpc.text("client_lua", pid=pid, target="system",
                                              code=f"System.SetCVar('{name}',{value}) return (System.GetCVar('{name}'))")
                            if actual == "nil" or float(actual) != value:
                                raise RuntimeError(f"CVar {name} was not applied: {actual}")
                        world_connected = time.monotonic()
                        rpc.text("client_lua", pid=pid, target="ui", code="X2World:EnterWorld(1,0)")
                        wait_stage(rpc, pid, 4, args.timeout)
                        character_seconds = round(time.monotonic() - world_connected, 3)
                        shot(rpc, pid, args.output_dir / f"{label}-characters.png")
                        if args.verify_pak:
                            pak_verification = json.loads(rpc.text("client_pak", pid=pid, action="verify"))
                        if args.pak_profile:
                            rpc.text("client_pak", pid=pid, action="profile")
                        world_started = time.monotonic()
                        rpc.text("client_lua", pid=pid, target="ui", code="local b=characterSelectWindow.bottomPanel.startButton b:OnClick('LeftButton')")
                        wait_player(rpc, pid, args.timeout)
                        world_seconds = round(time.monotonic() - world_started, 3)
                        if args.pak_profile:
                            rpc.text("client_pak", pid=pid, action="off")
                            pak_stats = json.loads(rpc.text("client_pak", pid=pid, action="status"))
                            (args.output_dir / f"{label}-pak.csv").write_text(
                                rpc.text("client_pak", pid=pid, action="trace"), encoding="utf-8")
                        # Give the first in-world frame time to finish loading the HUD.
                        time.sleep(2)
                        shot(rpc, pid, args.output_dir / f"{label}-world.png")
                    if args.hidden:
                        rpc.tool("client_window", pid=pid, visible=True)
                        rpc.tool("client_window", pid=pid, visible=False)
                    entry = {"profile": label, "fast_pak": fast_pak, "smooth_start": smooth, "seconds_to_server_select": seconds, "hidden": args.hidden,
                             "status": status, "screenshot": str((args.output_dir / f"{label}.png").resolve())}
                    if priority_samples is not None:
                        entry["priority_classes"] = sorted(set(s[1] for s in priority_samples))
                        (args.output_dir / f"{label}-priority.json").write_text(json.dumps(priority_samples), encoding="utf-8")
                    if args.enter_world:
                        entry.update(seconds_to_character_select=character_seconds,
                                     seconds_to_world=world_seconds, cvars=cvars)
                        if args.pak_profile:
                            entry["pak"] = pak_stats
                        if args.verify_pak:
                            entry["pak_verification"] = pak_verification
                        client_log = pathlib.Path.home() / "Documents" / "ArcheAge" / "ArcheAge.log"
                        if client_log.exists():
                            (args.output_dir / f"{label}-ArcheAge.log").write_bytes(client_log.read_bytes())
                    measurements.append(entry)
                    print(json.dumps(entry), flush=True)
                    (args.output_dir / "measurements.json").write_text(json.dumps(measurements, indent=2), encoding="utf-8")
                finally:
                    if priority:
                        priority.finish()
                    if pid is not None:
                        rpc.tool("client_stop", pid=pid)
                        time.sleep(1)
        profile_names = dict.fromkeys(m["profile"].rsplit("-", 1)[0] for m in measurements)
        for profile in profile_names:
            runs = [m for m in measurements if m["profile"].rsplit("-", 1)[0] == profile]
            values = [m["seconds_to_server_select"] for m in runs]
            print(f"{profile}: startup median {statistics.median(values):.3f}s ({len(values)} runs)", flush=True)
            world_values = [m["seconds_to_world"] for m in runs if "seconds_to_world" in m]
            if world_values:
                print(f"{profile}: world median {statistics.median(world_values):.3f}s", flush=True)
    finally:
        rpc.close()


if __name__ == "__main__":
    main()
