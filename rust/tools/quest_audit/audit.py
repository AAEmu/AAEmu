"""Read-only quest accounting for compact.sqlite3; does not certify playability.

Run from any directory: python audit.py --db PATH --output PATH.json
The ledger includes every quest context, every component/base act, and every detail
row. Loader mappings and objective classifications come from the Rust sources.
Behavior finding rules record the October 13, 2026 source review; maintain them
alongside engine fixes. They are not a substitute for executing quest lifecycles.
"""

import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import re
import sqlite3


REPO = Path(__file__).resolve().parents[3]
QUESTS = REPO / "rust/crates/aaemu-game/src/systems/quests"
TUTORIAL = 45
UNIMPLEMENTED = {
    "QuestActConFail", "QuestActObjCondition", "QuestActObjDoodadPhaseCheck",
    "QuestActObjEffectFire", "QuestActObjSendMail", "QuestActObjZoneNpcTalk",
    "QuestActObjZoneQuestComplete", "QuestActSupplyAaPoint",
    "QuestActSupplyInteraction", "QuestActSupplySkill",
}


def truth(value):
    return value in ("t", "true", 1, True)


def source_model():
    data = (QUESTS / "data.rs").read_text(encoding="utf-8")
    templates = (QUESTS / "templates.rs").read_text(encoding="utf-8")
    model = (QUESTS / "model.rs").read_text(encoding="utf-8")
    reqs = (QUESTS / "unit_reqs.rs").read_text(encoding="utf-8")
    tables = {
        cls: table for table, cls in re.findall(
            r'self\.load_table\(\s*conn,\s*"([^"]+)",\s*"([^"]+)"', data
        )
    }
    variants = dict(re.findall(r'(\w+)\s*(?:\{[^\n]*\})?\s*=>\s*"(QuestAct\w+)"', templates))
    objective_body = templates.split("pub fn counts_as_objective", 1)[1].split(
        "/// The generic item", 1
    )[0]
    objectives = {variants[v] for v in re.findall(r'(\w+)\s*\{', objective_body) if v in variants}
    kinds = {name: int(value) for name, value in re.findall(
        r"pub const (\w+): u8 = (\d+);", model.split("pub mod status", 1)[0]
    )}
    known_reqs = {int(value, 16): name for name, value in re.findall(
        r"pub const (\w+): u32 = (0x[0-9A-F]+);", reqs
    )}
    if len(tables) != len(variants) or not objectives or kinds.get("START") != 2:
        raise ValueError("Rust source format changed; update the audit parser")
    return tables, objectives, kinds, known_reqs


def rows(conn, table):
    # Identifiers come from sqlite_master / audited source, never CLI input.
    return [dict(row) for row in conn.execute('SELECT * FROM "' + table.replace('"', '""') + '"')]


def audit(conn):
    tables, objectives, kinds, known_reqs = source_model()
    contexts = rows(conn, "quest_contexts")
    components = rows(conn, "quest_components")
    base_acts = rows(conn, "quest_acts")
    by_quest = defaultdict(list)
    by_component = defaultdict(list)
    for component in components:
        by_quest[component["quest_context_id"]].append(component)
    for act in base_acts:
        by_component[act["quest_component_id"]].append(act)
    context_ids = {q["id"] for q in contexts}
    loaded_context_ids = {q["id"] for q in contexts if q["category_id"] != TUTORIAL}
    loaded_component_ids = {c["id"] for c in components if c["quest_context_id"] in loaded_context_ids}
    component_ids = {c["id"] for c in components}
    details = {cls: {row["id"]: row for row in rows(conn, table)} for cls, table in tables.items()}
    table_order = {cls: index for index, cls in enumerate(tables)}
    detail_order = {cls: {did: index for index, did in enumerate(detail)} for cls, detail in details.items()}
    id_sets = {table: {r["id"] for r in rows(conn, table)}
               for table in ("npcs", "items", "doodad_almighties", "quest_item_groups", "quest_monster_groups", "spheres")}
    npc_members = rows(conn, "quest_monster_npcs")
    item_members = rows(conn, "quest_item_group_items")
    requirements = defaultdict(list)
    for req in rows(conn, "unit_reqs"):
        requirements[(req["owner_type"], req["owner_id"])].append(req)
    starters = defaultdict(list)
    for row in rows(conn, "item_accept_quests"):
        starters[row["quest_id"]].append(row["item_id"])
    findings = []
    counts = Counter()
    type_counts = Counter()
    all_referenced_details = defaultdict(set)
    live_referenced_details = defaultdict(set)
    live_components = set()
    quest_ledger = []
    resolved = defaultdict(set)
    first_detail = {}
    for act in sorted(base_acts, key=lambda a: (a["quest_component_id"], a["id"])):
        if act["quest_component_id"] in loaded_component_ids:
            first_detail.setdefault((act["act_detail_type"], act["act_detail_id"]), act["id"])

    def finding(q, code, severity, evidence):
        findings.append({"quest_id": q["id"], "code": code, "severity": severity, "evidence": evidence})
        q["findings"].append(code)

    for quest in sorted(contexts, key=lambda q: q["id"]):
        qid = quest["id"]
        tutorial = quest["category_id"] == TUTORIAL
        authored = sorted(by_quest[qid], key=lambda c: (c["component_kind_id"], c["id"]))
        q = {"id": qid, "name": quest["name"], "category_id": quest["category_id"],
             "detail_id": quest["detail_id"], "zone_id": quest["zone_id"],
             "repeatable": truth(quest["REPEATABLE"]), "selective": truth(quest["selective"]),
             "let_it_done": truth(quest["let_it_done"]), "score": quest["score"] or 0,
             "loader_status": "excluded_tutorial" if tutorial else "loaded",
             "validation_status": "excluded_tutorial" if tutorial else "static_checks_only",
             "starter_items": sorted(starters[qid]), "components": [], "findings": []}
        slots = Counter()
        no_start = not any(c["component_kind_id"] == kinds["START"] for c in authored)
        auxiliary = None
        if quest["category_id"] == 34 and quest["detail_id"] == 6 and 2148 <= qid <= 2229:
            auxiliary = "Reserved task context (authored reserve label, category 34, task detail)"
        elif quest["category_id"] == 28 and quest["detail_id"] == 4 and no_start:
            auxiliary = "Tutorial reward/support definition; no standalone acceptance component"
        elif qid in (315, 1728, 2046, 1576, 1830, 1831) and no_start:
            auxiliary = "Authored skill/doodad/unit-requirement dummy or explicitly unused context"
        if not tutorial and auxiliary:
            q["disposition"] = auxiliary
            q["validation_status"] = "auxiliary_context"
        elif not tutorial and no_start:
            finding(q, "no_components" if not authored else "no_start_step", "blocker", "No authored START component; disposition needs content evidence")
        for component in authored:
            cid, step = component["id"], component["component_kind_id"]
            if not tutorial:
                live_components.add(cid)
            cc = {"id": cid, "step": step, "requirements": requirements[("QuestComponent", cid)],
                  "authored": component, "acts": []}
            raw_acts = sorted(by_component[cid], key=lambda a: a["id"])
            # Detail table load order, rather than quest_acts.id, determines wire slots.
            raw_acts.sort(key=lambda a: (table_order.get(a["act_detail_type"], len(tables)),
                                        detail_order.get(a["act_detail_type"], {}).get(a["act_detail_id"], -1)))
            for act in raw_acts:
                cls, did = act["act_detail_type"], act["act_detail_id"]
                all_referenced_details[cls].add(did)
                detail = details.get(cls, {}).get(did)
                index = slots[step] if cls in objectives else None
                aa = {"id": act["id"], "class": cls, "detail_id": did,
                      "detail": detail, "objective_slot": index}
                cc["acts"].append(aa)
                if cls in objectives:
                    slots[step] += 1
                if tutorial:
                    continue
                type_counts[cls] += 1
                live_referenced_details[cls].add(did)
                if detail is None:
                    finding(q, "missing_act_detail", "blocker", aa)
                if first_detail.get((cls, did)) != act["id"]:
                    finding(q, "duplicate_detail_link", "blocker", aa)
                if cls in UNIMPLEMENTED:
                    finding(q, "unimplemented_act", "blocker", aa)
                if index is not None and index >= 5:
                    finding(q, "objective_slot_overflow", "blocker", {"component": cid, "act": aa})
                if cls == "QuestActObjAggro":
                    resolved["aggro_victim_credit"].add(qid)
                if cls == "QuestActCheckSphere":
                    resolved["check_sphere_local_presence"].add(qid)
                if cls == "QuestActCheckGuard":
                    resolved["escort_npc_death_failure"].add(qid)
                if cls == "QuestActConAcceptComponent":
                    finding(q, "component_accept_always_true", "review", {"component": cid, "detail": detail})
                if cls == "QuestActObjAbilityLevel":
                    resolved["ability_level_subscription_and_objective"].add(qid)
                if cls == "QuestActObjCompleteQuest":
                    resolved["quest_completion_subscription"].add(qid)
                if cls == "QuestActObjZoneKill":
                    resolved["zone_kill_zone_filter"].add(qid)
                if cls == "QuestActObjInteraction" and detail and (detail.get("wi_id") or detail.get("phase")):
                    finding(q, "interaction_ignores_action_or_phase", "review", {"component": cid, "detail": detail})
                if cls == "QuestActCheckTimer":
                    resolved["timer_deadline_save_reload_and_offline_expiry"].add(qid)
                    resolved["timer_event_isolation"].add(qid)
                if cls == "QuestActConReportNpc":
                    resolved["explicit_npc_report_and_quest_isolation"].add(qid)
                if cls == "QuestActConReportDoodad":
                    resolved["doodad_report_isolation_selection_and_unsubscribe"].add(qid)
                if detail:
                    for field, target in (("npc_id", "npcs"), ("item_id", "items"),
                                          ("doodad_id", "doodad_almighties"), ("sphere_id", "spheres"),
                                          ("item_group_id", "quest_item_groups"),
                                          ("quest_item_group_id", "quest_item_groups"),
                                          ("npc_group_id", "quest_monster_groups"),
                                          ("quest_monster_group_id", "quest_monster_groups")):
                        value = detail.get(field)
                        if value and value not in id_sets[target]:
                            finding(q, "missing_act_target", "review", {"act": act["id"], "class": cls,
                                    "field": field, "target_table": target, "target_id": value})
                if detail and truth(detail.get("cleanup")) and truth(detail.get("destroy_when_drop")) and step != kinds["REWARD"]:
                    resolved["completion_cleanup_once"].add(qid)
            if not tutorial:
                if truth(component["or_unit_reqs"]) and cc["requirements"]:
                    resolved["component_or_requirements"].add(qid)
                for req in cc["requirements"]:
                    if req["kind_id"] not in known_reqs:
                        finding(q, "unknown_requirement_passes", "review", req)
                    if req["kind_id"] in (0x2C, 0x2E, 0x36, 0x44):
                        finding(q, "requirement_always_true", "review", req)
                    if req["kind_id"] in (0x1F, 0x20, 0x21, 0x24, 0x25, 0x48, 0x49) and req["value1"] not in context_ids:
                        finding(q, "missing_required_quest", "review", req)
                if component["npc_ai_id"] in (2, 3, 5, 6) or component["npc_spawner_id"] or component["ai_command_set_id"]:
                    finding(q, "authored_npc_actions_need_verification", "review", {"component": cid,
                            "npc_ai_id": component["npc_ai_id"], "npc_spawner_id": component["npc_spawner_id"],
                            "ai_command_set_id": component["ai_command_set_id"]})
            q["components"].append(cc)
        q["findings"] = sorted(set(q["findings"]))
        if q["findings"]:
            q["validation_status"] = "blocked" if any(f["severity"] == "blocker" for f in findings if f["quest_id"] == qid) else "needs_review"
        quest_ledger.append(q)
        counts[q["validation_status"]] += 1

    orphan_components = [c for c in components if c["quest_context_id"] not in context_ids]
    orphan_acts = [a for a in base_acts if a["quest_component_id"] not in component_ids]
    orphan_component_ids = {c["id"] for c in orphan_components}
    acts_under_orphans = [a for a in base_acts if a["quest_component_id"] in orphan_component_ids]
    for act in orphan_acts + acts_under_orphans:
        all_referenced_details[act["act_detail_type"]].add(act["act_detail_id"])
    detail_ledger = {cls: {"table": table, "rows": len(details[cls]),
                          "live_referenced_rows": len(live_referenced_details[cls]),
                          "unreferenced_ids": sorted(set(details[cls]) - all_referenced_details[cls]),
                          "not_loaded_ids": sorted(set(details[cls]) - live_referenced_details[cls])}
                     for cls, table in tables.items()}
    tutorial_components = sum(len(q["components"]) for q in quest_ledger if q["loader_status"] == "excluded_tutorial")
    tutorial_acts = sum(len(c["acts"]) for q in quest_ledger if q["loader_status"] == "excluded_tutorial" for c in q["components"])
    live_act_count = sum(type_counts.values())
    assert len(components) == len(live_components) + tutorial_components + len(orphan_components)
    assert len(base_acts) == live_act_count + tutorial_acts + len(orphan_acts) + len(acts_under_orphans)
    finding_counts = {code: {"quests": len({f["quest_id"] for f in findings if f["code"] == code}),
                             "occurrences": sum(f["code"] == code for f in findings)}
                      for code in sorted({f["code"] for f in findings})}
    return {"scope": "Static source/data audit, not a live playthrough certificate",
            "behavior_rules_reviewed_on": "2026-10-13",
            "summary": {"contexts": len(contexts), "loaded_quests": len(contexts) - counts["excluded_tutorial"],
                        "excluded_tutorials": counts["excluded_tutorial"], "components": len(components),
                        "loaded_components": len(live_components), "tutorial_components": tutorial_components,
                        "orphan_components": len(orphan_components), "base_acts": len(base_acts),
                        "loaded_acts": live_act_count, "tutorial_acts": tutorial_acts,
                        "orphan_acts": len(orphan_acts), "acts_under_orphan_components": len(acts_under_orphans),
                        "detail_tables_loaded": len(tables), "act_types_in_loaded_quests": len(type_counts),
                        "validation_status": dict(counts), "findings": finding_counts},
            "act_counts": dict(sorted(type_counts.items())), "quests": quest_ledger, "findings": findings,
            "resolved_findings": {code: {"quests": len(ids), "quest_ids": sorted(ids)} for code, ids in resolved.items()},
            "orphan_components": orphan_components, "orphan_acts": orphan_acts,
            "acts_under_orphan_components": acts_under_orphans, "detail_tables": detail_ledger,
            "npc_group_members": npc_members, "item_group_members": item_members,
            "missing_npc_group_members": [r for r in npc_members if r["npc_id"] not in id_sets["npcs"]],
            "quest_supplies": rows(conn, "quest_supplies"), "item_starters": rows(conn, "item_accept_quests"),
            "db_spheres": rows(conn, "spheres"), "db_sphere_quests": rows(conn, "sphere_quests"),
            "quest_and_sphere_requirements": [req for (owner, _), reqs in requirements.items()
                                              if owner in ("QuestComponent", "Sphere") for req in reqs],
            "starter_bindings_to_missing_quests": {str(q): items for q, items in starters.items() if q not in context_ids}}


def merge_spheres(result, export):
    """Missing sign geometry is review evidence, not a claim about client-driven starters."""
    result["client_spheres"] = export
    spheres_by_quest = defaultdict(list)
    for sphere in export["sign_spheres"]:
        spheres_by_quest[sphere["quest_id"]].append(sphere)
    missing = defaultdict(list)
    for record in export["summary"]["missing_sphere_components"]:
        missing[record["quest_id"]].append(record)
    for quest in result["quests"]:
        quest["client_spheres"] = spheres_by_quest[quest["id"]]
        for record in missing.get(quest["id"], []):
            result["findings"].append({"quest_id": quest["id"], "code": "missing_client_sign_sphere",
                                       "severity": "blocker" if record["class"] != "QuestActConAcceptSphere" else "review", "evidence": record})
            quest["findings"].append("missing_client_sign_sphere")
            if record["class"] != "QuestActConAcceptSphere":
                quest["validation_status"] = "blocked"
            elif quest["validation_status"] == "static_checks_only":
                quest["validation_status"] = "needs_review"
        quest["findings"] = sorted(set(quest["findings"]))
    result["summary"]["validation_status"] = dict(Counter(q["validation_status"] for q in result["quests"]))
    result["summary"]["findings"]["missing_client_sign_sphere"] = {
        "quests": len(missing), "occurrences": sum(map(len, missing.values()))}
    result["summary"]["client_sign_spheres"] = dict(Counter(
        "matching_component" if s["matching_component"] else
        "mismatched_quest" if s["loaded_component"] else "component_not_loaded"
        for s in export["sign_spheres"]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--spheres", type=Path, help="Optional export from real_compact_db_quest_accounting")
    args = parser.parse_args()
    db = args.db.resolve(strict=True)
    if args.output.resolve() == db:
        parser.error("Output must not overwrite the source database")
    with sqlite3.connect(db.as_uri() + "?mode=ro", uri=True) as conn:
        conn.row_factory = sqlite3.Row
        result = audit(conn)
    with db.open("rb") as stream:
        result["database"] = {"path": str(db), "sha256": hashlib.file_digest(stream, "sha256").hexdigest()}
    if args.spheres:
        merge_spheres(result, json.loads(args.spheres.read_text(encoding="utf-8")))
        result["client_sphere_export"] = {"path": str(args.spheres.resolve()),
                                         "sha256": hashlib.sha256(args.spheres.read_bytes()).hexdigest()}
    result["sources"] = {name: hashlib.sha256((QUESTS / name).read_bytes()).hexdigest()
                         for name in ("data.rs", "templates.rs", "acts.rs", "engine.rs", "unit_reqs.rs", "spheres.rs")}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["summary"], indent=2))


if __name__ == "__main__":
    main()
