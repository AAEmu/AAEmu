"""Accounting tests with deliberately broken miniature quest data."""

import sqlite3
import unittest

from audit import audit, merge_spheres, source_model


def database():
    conn = sqlite3.connect(":memory:")
    conn.row_factory = sqlite3.Row
    conn.executescript("""
        CREATE TABLE quest_contexts (id INTEGER, name TEXT, category_id INTEGER,
            REPEATABLE TEXT, detail_id INTEGER, zone_id INTEGER, selective TEXT,
            let_it_done TEXT, score INTEGER);
        CREATE TABLE quest_components (id INTEGER, quest_context_id INTEGER,
            component_kind_id INTEGER, npc_ai_id INTEGER, npc_spawner_id INTEGER,
            ai_command_set_id INTEGER, or_unit_reqs TEXT);
        CREATE TABLE quest_acts (id INTEGER, quest_component_id INTEGER,
            act_detail_id INTEGER, act_detail_type TEXT);
        CREATE TABLE unit_reqs (id INTEGER, owner_type TEXT, owner_id INTEGER,
            kind_id INTEGER, value1 INTEGER, value2 INTEGER);
        CREATE TABLE item_accept_quests (item_id INTEGER, quest_id INTEGER);
        CREATE TABLE quest_monster_npcs (quest_monster_group_id INTEGER, npc_id INTEGER);
        CREATE TABLE quest_item_group_items (quest_item_group_id INTEGER, item_id INTEGER);
        CREATE TABLE quest_supplies (id INTEGER);
        CREATE TABLE sphere_quests (id INTEGER);
    """)
    for table in source_model()[0].values():
        conn.execute(f"CREATE TABLE {table} (id INTEGER, count INTEGER)")
    for table in ("npcs", "items", "doodad_almighties", "quest_item_groups", "quest_monster_groups", "spheres"):
        conn.execute(f"CREATE TABLE {table} (id INTEGER)")
    return conn


def quest(conn, qid, category=1):
    conn.execute("INSERT INTO quest_contexts VALUES (?, 'fixture', ?, 'f', 1, 1, 'f', 'f', 0)", (qid, category))


def component(conn, cid, qid, step=2):
    conn.execute("INSERT INTO quest_components VALUES (?, ?, ?, 1, 0, 0, 'f')", (cid, qid, step))


def act(conn, aid, cid, detail_id, cls="QuestActObjMonsterHunt", detail=True):
    conn.execute("INSERT INTO quest_acts VALUES (?, ?, ?, ?)", (aid, cid, detail_id, cls))
    if detail:
        conn.execute(f"INSERT INTO {source_model()[0][cls]} VALUES (?, 1)", (detail_id,))


class QuestAuditTests(unittest.TestCase):
    def test_accounts_for_tutorials_orphans_and_unreferenced_details(self):
        with database() as conn:
            quest(conn, 1)
            quest(conn, 2, category=45)
            component(conn, 10, 1)
            component(conn, 20, 2)
            component(conn, 30, 999)
            act(conn, 1, 10, 1)
            act(conn, 2, 20, 2)
            act(conn, 3, 30, 3)
            act(conn, 4, 888, 4)
            conn.execute("INSERT INTO quest_act_obj_monster_hunts VALUES (5, 1)")
            result = audit(conn)
            summary = result["summary"]
            self.assertEqual((summary["contexts"], summary["loaded_quests"], summary["excluded_tutorials"]), (2, 1, 1))
            self.assertEqual((summary["loaded_components"], summary["tutorial_components"], summary["orphan_components"]), (1, 1, 1))
            self.assertEqual((summary["loaded_acts"], summary["tutorial_acts"], summary["orphan_acts"], summary["acts_under_orphan_components"]), (1, 1, 1, 1))
            detail = result["detail_tables"]["QuestActObjMonsterHunt"]
            self.assertEqual(detail["unreferenced_ids"], [5])
            self.assertEqual(detail["not_loaded_ids"], [2, 3, 4, 5])

    def test_detects_missing_and_reused_details_across_quests(self):
        with database() as conn:
            for qid in (1, 2):
                quest(conn, qid)
                component(conn, qid * 10, qid)
            act(conn, 1, 10, 1)
            act(conn, 2, 20, 1, detail=False)
            act(conn, 3, 20, 999, detail=False)
            result = audit(conn)
            codes = {(f["quest_id"], f["code"]) for f in result["findings"]}
            self.assertIn((2, "duplicate_detail_link"), codes)
            self.assertIn((2, "missing_act_detail"), codes)

    def test_objective_slots_are_shared_between_components_of_same_step(self):
        with database() as conn:
            quest(conn, 1)
            component(conn, 10, 1)
            component(conn, 11, 1, step=4)
            component(conn, 12, 1, step=4)
            for index in range(6):
                act(conn, index + 1, 11 if index < 3 else 12, index + 1)
            result = audit(conn)
            findings = [f for f in result["findings"] if f["code"] == "objective_slot_overflow"]
            self.assertEqual(len(findings), 1)
            self.assertEqual(findings[0]["evidence"]["act"]["objective_slot"], 5)

    def test_unknown_requirement_and_unused_act_are_flagged(self):
        with database() as conn:
            quest(conn, 1)
            component(conn, 10, 1)
            act(conn, 1, 10, 1, cls="QuestActObjCondition")
            conn.execute("INSERT INTO unit_reqs VALUES (1, 'QuestComponent', 10, 999, 0, 0)")
            result = audit(conn)
            self.assertEqual({f["code"] for f in result["findings"]}, {"unimplemented_act", "unknown_requirement_passes"})
            self.assertEqual(result["quests"][0]["validation_status"], "blocked")

    def test_missing_client_geometry_does_not_certify_or_block_client_driven_starters(self):
        with database() as conn:
            quest(conn, 1)
            component(conn, 10, 1)
            quest(conn, 2)
            component(conn, 20, 2)
            result = audit(conn)
            merge_spheres(result, {"sign_spheres": [], "summary": {"missing_sphere_components": [
                {"quest_id": 1, "component_id": 10, "class": "QuestActConAcceptSphere"}
            ]}})
            self.assertEqual(result["quests"][0]["validation_status"], "needs_review")
            self.assertEqual(result["quests"][1]["validation_status"], "static_checks_only")
            self.assertEqual(result["summary"]["findings"]["missing_client_sign_sphere"]["quests"], 1)


if __name__ == "__main__":
    unittest.main()
