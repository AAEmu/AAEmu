#!/usr/bin/env python3
"""Exercise a mounted car or boat through the real client's MCP and save telemetry.

Use a disposable test character with room to drive or deep water to sail. Summoning is
optional; an owned scroll is cast through the normal item skill, then the character
approaches and interacts with the vehicle. Never writes client assets or sends desktop keys.
"""
import argparse
import importlib.util
import json
import math
import pathlib
import sys
import time

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("client_smoke", pathlib.Path(__file__).with_name("client-smoke.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--driver-exe", type=pathlib.Path, required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--kind", choices=["car", "boat"], required=True)
    parser.add_argument("--entity-name", help="Saved vehicle name; defaults to the mounted vehicle's name")
    parser.add_argument("--expected-vehicle", help="Expected client description, e.g. Harpoon Clipper")
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument("--summon-item", type=int, help="Owned scroll template; omit for an already mounted vehicle")
    parser.add_argument("--dismiss-item", type=int, help="Dismiss an already mounted vehicle after testing with its owned scroll")
    parser.add_argument("--approach-key", choices=["w", "q", "e"], default="w")
    parser.add_argument("--approach-ms", type=int, default=1000)
    parser.add_argument("--settle-seconds", type=float, default=0,
                        help="Record idle transforms before driving and after releasing reverse (0..60 seconds)")
    args = parser.parse_args()
    if args.summon_item and args.dismiss_item:
        parser.error("--summon-item already includes dismissal; omit --dismiss-item")
    if not 1 <= args.approach_ms <= 10000 or not 0 <= args.settle_seconds <= 60:
        parser.error("approach-ms must be 1..10000 and settle-seconds must be 0..60")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    rpc = smoke.Rpc([str(args.driver_exe), "mcp", "--artifact-dir", str(args.output_dir)])
    samples = []
    report = {"kind": args.kind, "samples": samples, "passed": False}

    def tool(tool_name, **kw):
        result = rpc.tool(tool_name, pid=args.pid, **kw)
        return result.get("structuredContent", "\n".join(c.get("text", "") for c in result["content"]))

    def wait(predicate, timeout):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                return
            time.sleep(.25)
        raise RuntimeError("vehicle state did not arrive before timeout")

    def entity():
        result = tool("client_entities", name=args.entity_name, **{"class": "prefab_unit"})
        if result["count"] != 1:
            raise RuntimeError(f"expected one {args.entity_name}, found {result['count']}")
        return next(iter(result["entities"].values()))

    def sample(segment, duration):
        start = time.monotonic()
        while time.monotonic() - start < duration:
            state, body = tool("client_vehicle"), entity()
            if not state["driver"]:
                raise RuntimeError("driver became detached")
            numbers = [*body["position"].values(), *body["angles"].values(), *body["velocity"].values(), state["speed"]]
            if not all(isinstance(v, (float, int)) and math.isfinite(v) for v in numbers):
                raise RuntimeError("non-finite vehicle transform or speed")
            samples.append({"segment": segment, "t": time.monotonic(), "vehicle": state, "entity": body})
            time.sleep(.2)

    def press(key, duration):
        tool("client_input", key=key, duration_ms=duration)

    try:
        report["inventory"] = tool("client_inventory")
        if args.summon_item:
            if tool("client_vehicle")["has_vehicle"]:
                raise RuntimeError("dismiss the existing vehicle before testing a new summon")
            report["cast"] = tool("client_use_item", item_template_id=args.summon_item)
            if not report["cast"].get("accepted"):
                raise RuntimeError("client rejected item cast")
            wait(lambda: tool("client_vehicle")["has_vehicle"], 25)
            # Portals may finish after the owner's vehicle notification.
            time.sleep(8 if args.kind == "boat" else 1)
            press(args.approach_key, args.approach_ms)
            time.sleep(args.approach_ms / 1000 + .3)
            press("f", 100)
            wait(lambda: tool("client_vehicle")["driver"], 5)
        report["mounted"] = tool("client_vehicle")
        if not report["mounted"]["driver"]:
            raise RuntimeError("mount the driver's seat before running controls")
        if args.expected_vehicle and report["mounted"]["info"]["desc_name"] != args.expected_vehicle:
            raise RuntimeError(f"unexpected vehicle: {report['mounted']['info']}")
        args.entity_name = args.entity_name or report["mounted"]["info"]["name"]
        report["entity_name"] = args.entity_name
        if args.settle_seconds:
            sample("idle_before", args.settle_seconds)
        report["start"] = entity()
        tool("client_perf", action="profile")
        press("w", 4000)
        sample("accelerate", 4.4)
        press("w", 3000)
        press("a", 1500)
        sample("steer", 3.4)
        if args.kind == "car":
            press("space", 1800)
            sample("brake", 2.0)
        else:
            # Boats retain momentum after throttle release; don't assert car braking.
            sample("coast", 2.0)
        reverse_ms = 8000
        press("s", reverse_ms)
        sample("reverse", reverse_ms / 1000 + .4)
        report["frames"] = tool("client_perf", action="off")
        if args.settle_seconds:
            sample("idle_after", args.settle_seconds)
        report["finish"] = entity()
        displacement = math.hypot(report["finish"]["position"]["x"] - report["start"]["position"]["x"],
                                  report["finish"]["position"]["y"] - report["start"]["position"]["y"])
        peak = max(s["vehicle"]["speed"] for s in samples if s["segment"] == "accelerate")
        steer = [s["entity"]["angles"]["z"] for s in samples if s["segment"] == "steer"]
        yaw_change = abs(math.atan2(math.sin(steer[-1] - steer[0]), math.cos(steer[-1] - steer[0])))
        report["metrics"] = {"displacement_m": displacement, "forward_peak_mps": peak, "steering_yaw_change_rad": yaw_change}
        reverse = [s["vehicle"]["speed"] for s in samples if s["segment"] == "reverse"]
        report["metrics"]["reverse_min_mps"] = min(reverse)
        for segment in ("idle_before", "idle_after"):
            heights = [s["entity"]["position"]["z"] for s in samples if s["segment"] == segment]
            if heights:
                report["metrics"][segment + "_root_z_range_m"] = [min(heights), max(heights)]
        if displacement < 1 or peak < .5 or yaw_change < .01:
            raise RuntimeError(f"vehicle did not accelerate and turn: {report['metrics']}")
        if args.kind == "car":
            brake = [s["vehicle"]["speed"] for s in samples if s["segment"] == "brake"]
            if abs(brake[-1]) > .5 or min(reverse) > -.5:
                raise RuntimeError("car did not stop and reverse")
        elif min(reverse) > peak * .5:
            raise RuntimeError("boat reverse throttle did not reduce forward motion")
        smoke.shot(rpc, args.pid, args.output_dir / "vehicle.png")
        dismiss_item = args.summon_item or args.dismiss_item
        if dismiss_item:
            tool("client_vehicle", action="discard")
            wait(lambda: not tool("client_vehicle")["mounted"], 5)
            report["dismiss"] = tool("client_use_item", item_template_id=dismiss_item)
            wait(lambda: not tool("client_vehicle")["has_vehicle"], 15)
            wait(lambda: tool("client_entities", name=args.entity_name)["count"] == 0, 15)
            report["dismissed"] = True
        report["passed"] = True
        print(json.dumps({k: v for k, v in report.items() if k not in ("samples", "inventory")}, indent=2))
    except Exception as error:
        report["error"] = str(error)
        # Preserve the scene when terrain or an obstacle prevents the expected motion.
        try:
            tool("client_input", key="release")
            tool("client_perf", action="off")
            smoke.shot(rpc, args.pid, args.output_dir / "failure.png")
        except Exception:
            pass
        raise
    finally:
        try:
            tool("client_input", key="release")
            tool("client_perf", action="off")
        finally:
            rpc.close()
            (args.output_dir / "vehicle.json").write_text(json.dumps(report, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
