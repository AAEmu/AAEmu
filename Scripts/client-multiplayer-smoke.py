#!/usr/bin/env python3
"""Launch two disposable ArcheAge accounts and verify client-to-client replication.

Uses running local servers, filename-only passwords and PID-specific MCP calls. Existing
clients are preserved; only this MCP session's processes are stopped. Test characters are
kept for repeat runs. Never writes client assets or touches an existing user's character.
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


def quote(text):
    return '"' + ''.join(f"\\{byte:03}" for byte in text.encode("utf-8")) + '"'


def wait(check, timeout=30):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        last = check()
        if last:
            return last
        time.sleep(.25)
    raise RuntimeError(f"multiplayer condition timed out after {timeout}s: {last}")


def enter(rpc, pid, name, create_missing):
    def lua(code):
        return rpc.text("client_lua", pid=pid, target="ui", code=code)
    smoke.wait_stage(rpc, pid, 3, 90)
    lua("X2World:EnterWorld(1,0)")
    wait(lambda: lua("return X2LoginCharacter:GetCurrentStage()") in ("4", "5"), 60)
    stage = lua("return X2LoginCharacter:GetCurrentStage()")
    count = int(lua("return X2LoginCharacter:GetNumLoginCharacters()")) if stage == "4" else 0
    if count == 0:
        if not create_missing:
            raise RuntimeError("empty disposable account; rerun with --create-missing")
        if stage == "4":
            lua("X2LoginCharacter:CreateCharacter()")
        wait(lambda: lua("return characterCreate and characterCreate.state") == "Step1")
        time.sleep(1)
        lua("characterCreate.OnNextStep()")
        wait(lambda: lua("return characterCreate.state") == "Step2")
        time.sleep(1)
        lua("characterCreate.OnNextStep()")
        wait(lambda: lua("return characterCreate.state") == "Step3")
        # Follow the same verified 1.2 form and NewCharacterCBT2 path as the UI.
        lua(f"SelectAbility(1) step3.content.nameWnd.characterName:SetText({quote(name)}) CreateCharacter(false)")
        smoke.wait_stage(rpc, pid, 4, 60)
        count = int(lua("return X2LoginCharacter:GetNumLoginCharacters()"))
    names = [lua(f"return X2LoginCharacter:GetLoginCharacterName({i})") for i in range(1, count + 1)]
    if name not in names:
        raise RuntimeError(f"disposable character {name} not present: {names}")
    lua(f"X2LoginCharacter:SelectCharacter({names.index(name) + 1})")
    lua("characterSelectWindow.bottomPanel.startButton:OnClick('LeftButton')")
    smoke.wait_player(rpc, pid, 90)
    actual = lua("return X2Unit:UnitName('player')")
    if actual != name:
        raise RuntimeError(f"wrong active character: {actual}, expected {name}")
    return actual


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--driver-exe", type=pathlib.Path, required=True)
    parser.add_argument("--launcher", type=pathlib.Path, required=True)
    parser.add_argument("--dll", type=pathlib.Path, required=True)
    parser.add_argument("--client", type=pathlib.Path, required=True)
    parser.add_argument("--password-file", type=pathlib.Path, required=True)
    parser.add_argument("--password-file-b", type=pathlib.Path)
    parser.add_argument("--login", default="127.0.0.1:1237")
    parser.add_argument("--user-a", default="multitest_a")
    parser.add_argument("--user-b", default="multitest_b")
    parser.add_argument("--character-a", default="Multia")
    parser.add_argument("--character-b", default="Multib")
    parser.add_argument("--create-missing", action="store_true")
    parser.add_argument("--interaction", choices=["family", "trade"], default="family",
                        help="family invite/title/owner by default; stock 1.2 trade requires both characters level 10+")
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    args = parser.parse_args()
    users = [args.user_a, args.user_b]
    if len({u.casefold() for u in users}) != 2 or any(not u.startswith("multitest_") for u in users):
        parser.error("two distinct disposable multitest_ accounts are required")
    if args.character_a == args.character_b:
        parser.error("character names must be distinct")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    rpc = smoke.Rpc([str(args.driver_exe), "mcp", "--dll", str(args.dll), "--launcher", str(args.launcher),
                     "--client", str(args.client), "--login", args.login, "--password-file", str(args.password_file),
                     "--artifact-dir", str(args.output_dir)])
    report = {"passed": False, "clients": [], "checks": {}, "cleanup": []}
    pids = []
    def lua(pid, code):
        return rpc.text("client_lua", pid=pid, target="ui", code=code)
    def entities(pid, name):
        result = rpc.tool("client_entities", pid=pid, name=name)["structuredContent"]
        return [e for e in result["entities"].values() if e["name"] == name]
    def peer(pid, name):
        items = entities(pid, name)
        return items[0] if len(items) == 1 else None
    def events(pid):
        return lua(pid, "return table.concat(aaMultiplayerEvents,'\\n')")
    try:
        report["existing_clients"] = json.loads(rpc.text("client_list"))
        for user, name, password in zip(users, [args.character_a, args.character_b],
                                       [args.password_file, args.password_file_b or args.password_file]):
            launched = rpc.tool("client_launch", user=user, password_file=str(password), hidden=True)
            pid = launched["structuredContent"]["pid"]
            pids.append(pid)
            report["clients"].append({"pid": pid, "user": user, "character": name})
            enter(rpc, pid, name, args.create_missing)
            print(f"Entered {name} (PID {pid})", flush=True)
        a, b = pids
        names = [args.character_a, args.character_b]
        for pid, other in zip(pids, reversed(names)):
            report["checks"][f"pid_{pid}_sees_peer"] = wait(lambda: peer(pid, other))
            lua(pid, """aaMultiplayerEvents={} aaMultiplayerWindow=CreateEmptyWindow('aaMultiplayerObserver','UIParent')
aaMultiplayerWindow:Show(false) aaMultiplayerWindow:SetHandler('OnEvent',function(self,event,...)
 local row={event} for _,v in ipairs({...}) do row[#row+1]=tostring(v) end
 if #aaMultiplayerEvents<100 then aaMultiplayerEvents[#aaMultiplayerEvents+1]=table.concat(row,'|') end end)
aaMultiplayerWindow:RegisterEvent('CHAT_MESSAGE') aaMultiplayerWindow:RegisterEvent('TRADE_STARTED')
aaMultiplayerWindow:RegisterEvent('TRADE_CAN_START') aaMultiplayerWindow:RegisterEvent('TRADE_CANCELED')
aaMultiplayerWindow:RegisterEvent('FAMILY_MEMBER_ADDED') aaMultiplayerWindow:RegisterEvent('FAMILY_OWNER_CHANGED')
aaMultiplayerWindow:RegisterEvent('FAMILY_REFRESH') aaMultiplayerWindow:RegisterEvent('FAMILY_ERROR')
aaMultiplayerWindow:RegisterEvent('ERROR_MSG') aaMultiplayerWindow:RegisterEvent('SET_UI_MESSAGE')""")
        for mover, observer, name in [(a, b, args.character_a), (b, a, args.character_b)]:
            before = peer(observer, name)
            rpc.tool("client_input", pid=mover, key="w", duration_ms=1200)
            def moved():
                current = peer(observer, name)
                if current and math.dist(list(before["position"].values()), list(current["position"].values())) > .5:
                    return current
                return None
            after = wait(moved, 10)
            report["checks"][f"pid_{observer}_observes_movement"] = {"before": before, "after": after}
        token = f"aaemu-multiplayer-{time.time_ns()}"
        for sender, recipient in [(a, b), (b, a)]:
            message = f"{token}-{sender}"
            rpc.tool("client_chat", pid=sender, message=message)
            report["checks"][f"pid_{recipient}_received_chat"] = wait(lambda: message in events(recipient), 10)
        if args.interaction == "trade":
            levels = [int(lua(pid, "return X2Unit:UnitLevel('player')")) for pid in pids]
            if min(levels) < 10:
                raise RuntimeError(f"stock r208022 trade requires both characters level 10+, got {levels}; use family interaction or eligible fixtures")
            # Use the ordinary request with empty trade slots; no inventory changes.
            for _ in range(40):
                rpc.tool("client_action", pid=a, action="cycle_friendly_forward")
                time.sleep(.1)
                if lua(a, "return X2Unit:UnitName('target')") == args.character_b:
                    break
            else:
                raise RuntimeError("friendly target cycling did not select the other test character")
            report["checks"]["trade_target"] = lua(a, "return X2Unit:UnitName('target')")
            report["checks"]["trade_request_result"] = lua(a, "return X2Trade:RequestTrade()")
            report["checks"]["trade_received"] = wait(lambda: "TRADE_STARTED" in events(b), 10)
            lua(a, "X2Trade:CancelTrade(TCR_NORMAL)")
            report["checks"]["trade_cancelled"] = wait(lambda: "TRADE_CANCELED" in events(b), 10)
        else:
            family = [lua(pid, "return X2Family:IsFamily()") == "true" for pid in pids]
            if family == [False, False]:
                # This is the same OnOK(window ID) called by the original family
                # confirmation button. Record only the expected fixture's invitation.
                lua(b, "X2DialogManager:SetHandler(DLG_TASK_JOIN_FAMILY,function(wnd,info) aaMultiFamilyInviteId=wnd:GetId() aaMultiFamilyInvitor=info.invitorName end)")
                lua(a, f"X2Family:Invite({quote(args.character_b)},'Sibling')")
                wait(lambda: lua(b, "return aaMultiFamilyInvitor") == args.character_a, 10)
                lua(b, "X2DialogManager:OnOK(aaMultiFamilyInviteId)")
                wait(lambda: all(lua(pid, "return X2Family:IsFamily()") == "true" for pid in pids), 10)
                report["checks"]["family_invitation_accepted"] = True
            for pid, other in zip(pids, reversed(names)):
                if lua(pid, f"return X2Family:IsMyFamily({quote(other)})") != "true":
                    raise RuntimeError("the two fixtures must share their own family")
                if int(lua(pid, "return X2Family:GetMemberCount()").split("\t")[0]) != 2:
                    raise RuntimeError("family contains other characters; refusing to change its title/owner")
            owners = [lua(pid, "return X2Family:IsOwner()") == "true" for pid in pids]
            if owners.count(True) != 1:
                raise RuntimeError("family must have exactly one fixture owner")
            owner_index = owners.index(True)
            owner, new_owner = pids[owner_index], pids[1 - owner_index]
            new_owner_name = names[1 - owner_index]
            lua(owner, f"X2Family:ChangeTitle({quote(new_owner_name)},'SiblingTwo')")
            wait(lambda: all('SiblingTwo' in events(pid) for pid in pids), 10)
            lua(owner, f"X2Family:ChangeOwner({quote(new_owner_name)})")
            wait(lambda: lua(new_owner, "return X2Family:IsOwner()") == "true" and lua(owner, "return X2Family:IsOwner()") == "false", 10)
            report["checks"]["family_title_received_both"] = True
            report["checks"]["family_owner_changed_both"] = new_owner_name
        for pid in pids:
            report["checks"][f"pid_{pid}_events"] = events(pid)
            smoke.shot(rpc, pid, args.output_dir / f"client-{pid}-world.png")
        # Ambiguous calls must fail instead of silently operating on the wrong client.
        try:
            rpc.tool("client_status")
        except RuntimeError as error:
            if "supply pid" not in str(error):
                raise
            report["checks"]["ambiguous_pid_rejected"] = True
        else:
            raise RuntimeError("PID-less status unexpectedly selected a client")
        outsider = smoke.Rpc([str(args.driver_exe), "mcp"])
        try:
            try:
                outsider.tool("client_stop", pid=a)
            except RuntimeError as error:
                if "did not launch" not in str(error):
                    raise
                report["checks"]["unowned_client_stop_rejected"] = True
            else:
                raise RuntimeError("another MCP session stopped a client it did not own")
        finally:
            outsider.close()
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        for pid in pids:
            try:
                report["checks"][f"pid_{pid}_events"] = events(pid)
            except Exception:
                pass
            try:
                smoke.shot(rpc, pid, args.output_dir / f"client-{pid}-failure.png")
            except Exception:
                pass
        raise
    finally:
        # A launcher failure can still leave an owned client. Recover those PIDs from the
        # session list and clean only its retained handles, preserving pre-existing clients.
        try:
            owned = [p["pid"] for p in json.loads(rpc.text("client_list")) if p["launched_by_session"]]
            for pid in owned:
                try:
                    rpc.tool("client_input", pid=pid, key="release")
                except Exception:
                    pass
                try:
                    rpc.tool("client_stop", pid=pid)
                    report["cleanup"].append({"pid": pid, "stopped": True})
                except Exception as error:
                    report["cleanup"].append({"pid": pid, "error": str(error)})
            report["remaining_clients"] = json.loads(rpc.text("client_list"))
            if any(client["launched_by_session"] for client in report["remaining_clients"]):
                report["passed"] = False
                raise RuntimeError("an owned test client remained running after cleanup")
        finally:
            rpc.close()
            (args.output_dir / "multiplayer.json").write_text(json.dumps(report, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
