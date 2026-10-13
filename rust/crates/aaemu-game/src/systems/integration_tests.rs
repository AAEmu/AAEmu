//! Cross-subsystem flow tests (phase 3b wiring): every subsystem installs its hooks exactly as
//! at startup and the flows run through the real packet handlers / effects:
//!
//! * a player kills an NPC with a skill -> the quest kill objective advances, kill exp is
//!   awarded (skills -> npcs death -> quests / skills progress);
//! * a skill with an `InteractionEffect` used on a doodad -> the quest interaction objective
//!   advances (skills -> npcs effect + doodad use -> quests);
//! * a player force-attacks another player of the same faction -> crime evidence (skills ->
//!   social crimes -> npcs player doodad);
//! * a party member kills an NPC tagged by the party -> both members share the kill exp and
//!   get the quest credit; with `TagShareEnabled` a solo damage dealer gets the quest credit
//!   too (skills tagging -> npcs death -> social teams -> quests);
//! * the quest item / money hooks run under the character lock and reach the inventory once it
//!   is released (quests -> economy, `after_unlock`);
//! * the skills item hooks read and change the economy inventory (skills -> economy);
//! * a character death loses exp into recoverable exp (skills `Character.DoDie` -> economy
//!   durability).

use std::sync::Arc;

use crate::data::experience::{ExperienceTable, LevelTemplate};
use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::templates::DoodadTemplate;
use crate::systems::quests::model::kind;
use crate::systems::quests::templates::{ActKind, QuestComponentTemplate, QuestTemplate};
use crate::systems::skills::data::effects::EffectTemplate;
use crate::systems::skills::data::EffectKind;
use crate::systems::skills::handlers::{start_skill, CSStartSkillPacket};
use crate::systems::skills::tests::{damage_effect, skill_effect, skill_template};
use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{Entity, NpcRef, PlayerRef, WorldObject};

/// 600 damage (hostile target).
const HIT_SKILL: u32 = 10_001;
/// 1 000 000 damage (hostile target).
const KILL_SKILL: u32 = 10_002;
/// `InteractionEffect` (`WorldInteractionType.Use`) on a doodad.
const USE_SKILL: u32 = 10_003;
/// 50 damage with `CheckCrime`.
const CRIME_SKILL: u32 = 10_004;

/// NPC template of `npc_game_data` (level 10, KillExp 140).
const VILLAGER: u32 = 200;
const VILLAGER_KILL_EXP: i32 = 140;
const DOODAD_TEMPLATE: u32 = 1000;
/// `CrimeManager` small bloodstain evidence doodad.
const SMALL_BLOODSTAIN: u32 = 877;

const HUNT_QUEST: u32 = 100;
const INTERACTION_QUEST: u32 = 200;

/// `WorldInteractionType.Use`.
const WI_USE: i32 = 19;
/// A stackable item template (max 10) handed out by the quest item hooks.
const QUEST_ITEM: u32 = 5_000;

/// Quest `qid`: Start (auto) -> Progress (`act`, count 1) -> Ready (report to NPC 1).
fn single_act_quest(d: &mut crate::systems::quests::data::QuestData, qid: u32, act: ActKind) {
    d.add_template(QuestTemplate {
        id: qid,
        detail_id: crate::systems::quests::model::detail::NORMAL,
        ..QuestTemplate::default()
    });
    let component = |id: u32, kind_id: u8| QuestComponentTemplate {
        id,
        quest_id: qid,
        kind_id,
        npc_ai_id: 1,
        ..QuestComponentTemplate::default()
    };
    d.add_component(component(qid * 10 + 1, kind::START));
    d.add_act(
        qid * 10 + 1,
        qid * 10 + 1,
        qid * 10 + 1,
        0,
        ActKind::ConAutoComplete,
    );
    d.add_component(component(qid * 10 + 2, kind::PROGRESS));
    d.add_act(qid * 10 + 2, qid * 10 + 2, qid * 10 + 2, 1, act);
    d.add_component(component(qid * 10 + 3, kind::READY));
    d.add_act(
        qid * 10 + 3,
        qid * 10 + 3,
        qid * 10 + 3,
        0,
        ActKind::ConReportNpc {
            npc_id: 1,
            use_alias: false,
            alias_id: 0,
        },
    );
}

/// NPC data (`npc_game_data`) + an experience table + the test skills, doodads and quests.
fn game_data() -> GameData {
    let mut gd = crate::systems::npcs::npc::manager::tests::npc_game_data();
    gd.experience = ExperienceTable::from_templates(
        (1u8..=55)
            .map(|level| LevelTemplate {
                level,
                total_exp: (i32::from(level) - 1) * 10_000,
                total_mate_exp: (i32::from(level) - 1) * 1_000,
                skill_points: 0,
            })
            .collect(),
    )
    .expect("experience table");

    let s = &mut gd.systems.skills;
    s.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(damage_effect(1, 600))),
        900_001,
    );
    s.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(damage_effect(2, 1_000_000))),
        900_002,
    );
    s.add_effect(
        EffectKind::InteractionEffect,
        EffectTemplate::Interaction {
            id: 3,
            world_interaction: WI_USE,
            doodad_id: 0,
        },
        900_003,
    );
    let mut crime = damage_effect(4, 50);
    crime.check_crime = true;
    s.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(crime)),
        900_004,
    );
    for (skill_id, effect_type, kind, actual, target_type) in [
        (HIT_SKILL, 900_001, EffectKind::DamageEffect, 1, 4),
        (KILL_SKILL, 900_002, EffectKind::DamageEffect, 2, 4),
        (USE_SKILL, 900_003, EffectKind::InteractionEffect, 3, 8),
        (CRIME_SKILL, 900_004, EffectKind::DamageEffect, 4, 4),
    ] {
        let mut t = skill_template(skill_id);
        t.target_type = target_type;
        if target_type == 8 {
            t.damage_type_id = 0;
        }
        t.effects.push(skill_effect(effect_type, kind, actual));
        s.add_skill(t);
    }

    let d = &mut gd.systems.npcs.doodad;
    for id in [DOODAD_TEMPLATE, SMALL_BLOODSTAIN, SMALL_BLOODSTAIN + 1] {
        d.templates.insert(
            id,
            DoodadTemplate {
                id,
                ..DoodadTemplate::default()
            },
        );
    }

    let q = &mut gd.systems.quests;
    single_act_quest(
        q,
        HUNT_QUEST,
        ActKind::ObjMonsterHunt {
            npc_id: VILLAGER,
            use_alias: false,
            alias_id: 0,
            highlight_doodad_id: 0,
            highlight_doodad_phase: -1,
        },
    );
    single_act_quest(
        q,
        INTERACTION_QUEST,
        ActKind::ObjInteraction {
            wi_id: WI_USE,
            doodad_id: DOODAD_TEMPLATE,
            use_alias: false,
            team_share: false,
            highlight_doodad_id: 0,
            highlight_doodad_phase: -1,
            alias_id: 0,
            phase: 0,
        },
    );
    q.finish();
    gd.items.templates.insert(
        QUEST_ITEM,
        crate::data::items::ItemTemplate::plain(QUEST_ITEM, 10),
    );
    gd
}

/// A server with every subsystem's hooks installed (what `systems::start_all` installs).
fn server_with(tag_share: bool) -> Arc<GameServer> {
    let server = test_server_with(game_data());
    server
        .config
        .update(|c| c.world.tag_share_enabled = tag_share);
    let server = Arc::new(server);
    server.set_self();
    crate::systems::skills::bind(&server);
    crate::systems::npcs::install_hooks(&server);
    crate::systems::social::install_hooks(&server);
    crate::systems::quests::install_hooks(&server);
    crate::systems::economy::wiring::install(&server);
    crate::systems::economy::bridges::install(&server);
    server
}

struct Pc {
    client: TestClient,
    p: PlayerRef,
}

fn pc(server: &GameServer, id: u32, name: &str, x: f32) -> Pc {
    let mut client = TestClient::new(server, id, 1000 + id);
    let mut c = test_character(id, name, x, 100.0);
    c.experience = 90_000; // start of level 10
    c.unit.hp = 5_000;
    c.unit.max_hp = 5_000;
    c.unit.mp = 1_000;
    c.unit.max_mp = 1_000;
    c.is_online = true;
    let p = spawn_player(server, &client, c, 0x100 + id);
    client.conn.active_char = Some(p.clone());
    Pc { client, p }
}

/// An NPC of `VILLAGER` with `hp`, faction 0 (attackable by everyone).
fn villager(server: &GameServer, obj_id: u32, hp: i32) -> NpcRef {
    let npc = crate::systems::npcs::npc::manager::create(server, obj_id, VILLAGER).expect("npc");
    {
        let mut n = npc.data();
        n.unit.faction_id = 0;
        n.unit.hp = hp;
        n.unit.max_hp = hp;
        n.unit.transform.set_xyz(105.0, 100.0, 100.0);
    }
    let world = server.world.world(0).expect("world");
    world.spawn(&npc, &server.view_ctx());
    npc
}

fn cast(server: &GameServer, pc: &mut Pc, skill_id: u32, target: SkillCastTarget) {
    // No GCD between the test casts.
    pc.p.data().unit.skill_last_used_ms = 0;
    let packet = CSStartSkillPacket {
        skill_id,
        caster: SkillCaster::Unit(pc.p.obj_id),
        target,
        flag: 0,
        skill_object: SkillObject::None,
    };
    start_skill(server, &mut pc.client.conn, packet);
}

/// The active quest row of `quest_id`: (step, first objective).
fn quest_row(server: &GameServer, p: &PlayerRef, quest_id: u32) -> Option<(u8, i32)> {
    // QuestManagerRunQueueTask (the 1 ms evaluation queue task)
    server.systems.quests.run_queued_evaluations(server);
    p.data()
        .quests
        .active
        .iter()
        .find(|q| q.template_id == quest_id)
        .map(|q| (q.step, q.objectives[0]))
}

fn accept(server: &GameServer, p: &PlayerRef, quest_id: u32) {
    assert_eq!(
        crate::systems::quests::events::add_quest(server, p, quest_id, false),
        Some(true)
    );
    assert_eq!(quest_row(server, p, quest_id), Some((kind::PROGRESS, 0)));
}

#[tokio::test]
async fn skill_kill_advances_the_hunt_quest_and_awards_exp() {
    let server = server_with(false);
    let mut a = pc(&server, 1, "Hunter", 100.0);
    accept(&server, &a.p, HUNT_QUEST);
    let npc = villager(&server, 0x200, 1_000);
    a.client.drain();

    cast(
        &server,
        &mut a,
        KILL_SKILL,
        SkillCastTarget::Unit(npc.obj_id),
    );

    assert!(npc.data().unit.is_dead());
    // Npc.DoDie: the killer (no tagging rights holder) gets the kill exp and the hunt credit.
    assert_eq!(a.p.data().experience, 90_000 + VILLAGER_KILL_EXP);
    assert_eq!(quest_row(&server, &a.p, HUNT_QUEST), Some((kind::READY, 1)));
    let ops = a.client.drain_ops();
    for op in [
        sc::SCUnitDeathPacket,
        sc::SCExpChangedPacket,
        sc::SCQuestContextUpdatedPacket,
    ] {
        assert!(ops.contains(&op), "missing {op:#x} in {ops:x?}");
    }
    // Npc.DoDie tail: the player is out of combat. (As in C#, the damage effect adds the
    // killing blow's aggro / tagger to the dead NPC after `DoDie` cleared them.)
    assert!(!a.p.data().unit.is_in_battle);
}

#[tokio::test]
async fn interaction_skill_on_a_doodad_advances_the_interaction_quest() {
    let server = server_with(false);
    let mut a = pc(&server, 1, "User", 100.0);
    accept(&server, &a.p, INTERACTION_QUEST);
    let doodad = Entity::new(0x300, Doodad::new(DOODAD_TEMPLATE));
    doodad.data().transform.set_xyz(102.0, 100.0, 100.0);
    let world = server.world.world(0).expect("world");
    world.spawn(&doodad, &server.view_ctx());
    a.client.drain();

    cast(
        &server,
        &mut a,
        USE_SKILL,
        SkillCastTarget::Doodad(doodad.obj_id),
    );

    // InteractionEffect (npcs) -> QuestManager.DoDoodadInteractionEvents (quests).
    assert_eq!(
        quest_row(&server, &a.p, INTERACTION_QUEST),
        Some((kind::READY, 1))
    );
    assert!(a
        .client
        .drain_ops()
        .contains(&sc::SCQuestContextUpdatedPacket));
}

#[tokio::test]
async fn force_attacking_a_friendly_player_generates_crime_evidence() {
    let server = server_with(false);
    let mut a = pc(&server, 1, "Bandit", 100.0);
    let b = pc(&server, 2, "Victim", 103.0);
    a.p.data().unit.force_attack = true;
    a.client.drain();

    cast(
        &server,
        &mut a,
        CRIME_SKILL,
        SkillCastTarget::Unit(b.p.obj_id),
    );

    assert_eq!(b.p.data().unit.hp, 5_000 - 50);
    // Both sides remember the assault (no evidence for the retaliation).
    assert!(a.p.data().skill_char.assault_on.contains(&b.p.character_id));
    assert!(b
        .p
        .data()
        .skill_char
        .assaulted_by
        .contains(&a.p.character_id));
    // CrimeManager.GenerateEvidenceFromDamage -> DoodadManager.CreatePlayerDoodad: a small
    // bloodstain owned by the criminal, its data the victim's character id.
    let world = server.world.world(0).expect("world");
    let evidence: Vec<_> = world
        .all_doodads()
        .into_iter()
        .filter(|d| d.data().template_id == SMALL_BLOODSTAIN)
        .collect();
    assert_eq!(evidence.len(), 1, "one bloodstain");
    {
        let d = evidence[0].data();
        assert_eq!(d.owner_id, a.p.character_id);
        assert_eq!(d.data, b.p.character_id as i32);
    }
    // A second hit is not a new assault: no second evidence.
    cast(
        &server,
        &mut a,
        CRIME_SKILL,
        SkillCastTarget::Unit(b.p.obj_id),
    );
    let count = world
        .all_doodads()
        .into_iter()
        .filter(|d| d.data().template_id == SMALL_BLOODSTAIN)
        .count();
    assert_eq!(count, 1);
}

/// A and B form a party (invite + accept through the team manager).
fn form_party(server: &GameServer, a: &PlayerRef, b: &PlayerRef) -> u32 {
    let teams = &server.systems.social.teams;
    teams.ask_to_join(server, a, "", 0, true, Some(b));
    teams.reply_to_join_team(server, b, 0, true, a.character_id, false, "", false);
    let team = teams.active_team_by_unit(a.character_id).expect("party");
    assert!(team.is_member(b.character_id));
    team.id
}

#[tokio::test]
async fn party_member_kill_of_a_party_tagged_npc_shares_exp_and_quest_credit() {
    let server = server_with(false);
    let mut a = pc(&server, 1, "Lead", 100.0);
    let mut b = pc(&server, 2, "Mate", 102.0);
    let team_id = form_party(&server, &a.p, &b.p);
    accept(&server, &a.p, HUNT_QUEST);
    accept(&server, &b.p, HUNT_QUEST);
    let npc = villager(&server, 0x200, 1_000);

    // A deals 600 of 1000: more than half of the max hp by the party -> the party tags it.
    cast(
        &server,
        &mut a,
        HIT_SKILL,
        SkillCastTarget::Unit(npc.obj_id),
    );
    // 600 minus the armor reduction
    assert!(npc.data().unit.hp < 500);
    assert_eq!(npc.data().unit.skill_state.tagging.tag_team, team_id);
    a.client.drain();
    b.client.drain();

    // B lands the killing blow.
    cast(
        &server,
        &mut b,
        KILL_SKILL,
        SkillCastTarget::Unit(npc.obj_id),
    );
    assert!(npc.data().unit.is_dead());

    // Two eligible party members (party of 2): 90 % of the kill exp each.
    let share = (VILLAGER_KILL_EXP as f32 * 0.90) as i32;
    assert_eq!(a.p.data().experience, 90_000 + share);
    assert_eq!(b.p.data().experience, 90_000 + share);
    assert_eq!(quest_row(&server, &a.p, HUNT_QUEST), Some((kind::READY, 1)));
    assert_eq!(quest_row(&server, &b.p, HUNT_QUEST), Some((kind::READY, 1)));
    assert!(a.client.drain_ops().contains(&sc::SCExpChangedPacket));
    assert!(b.client.drain_ops().contains(&sc::SCExpChangedPacket));
}

#[tokio::test]
async fn tag_share_gives_quest_credit_to_a_solo_contributor() {
    for tag_share in [false, true] {
        let server = server_with(tag_share);
        let mut a = pc(&server, 1, "Lead", 100.0);
        let b = pc(&server, 2, "Mate", 102.0);
        let mut c = pc(&server, 3, "Solo", 104.0);
        form_party(&server, &a.p, &b.p);
        accept(&server, &c.p, HUNT_QUEST);
        let npc = villager(&server, 0x200, 1_000);

        // C hits first (no tagging rights: 50 % or less), the party tags and kills.
        cast(
            &server,
            &mut c,
            HIT_SKILL,
            SkillCastTarget::Unit(npc.obj_id),
        );
        npc.data().unit.hp = 1_000;
        cast(
            &server,
            &mut a,
            HIT_SKILL,
            SkillCastTarget::Unit(npc.obj_id),
        );
        cast(
            &server,
            &mut a,
            KILL_SKILL,
            SkillCastTarget::Unit(npc.obj_id),
        );
        assert!(npc.data().unit.is_dead());

        // XP is never shared with C.
        assert_eq!(c.p.data().experience, 90_000);
        let expected = if tag_share {
            Some((kind::READY, 1))
        } else {
            Some((kind::PROGRESS, 0))
        };
        assert_eq!(
            quest_row(&server, &c.p, HUNT_QUEST),
            expected,
            "TagShare {tag_share}"
        );
        let _ = WorldObject::from(&c.p);
    }
}

#[tokio::test]
async fn quest_locked_item_hooks_reach_the_inventory_after_unlock() {
    use crate::models::item_task::task_type;
    let server = server_with(false);
    let mut a = pc(&server, 1, "Quester", 100.0);
    a.client.drain();

    // A quest operation holds the character lock: the economy QuestHooks work on the
    // character's containers and flush their effects once the lock is released.
    let given = crate::systems::quests::events::with_quests(&server, &a.p, None, |ctx| {
        let p = ctx.player.clone();
        let given = ctx.hooks.acquire_default_item(
            ctx.server,
            p.as_ref(),
            ctx.c,
            task_type::QUEST_SUPPLY_ITEMS,
            QUEST_ITEM,
            3,
            0,
        );
        ctx.hooks.change_money(ctx.server, p.as_ref(), ctx.c, 250);
        let consumed = ctx.hooks.consume_item(
            ctx.server,
            p.as_ref(),
            ctx.c,
            task_type::QUEST_REMOVE_SUPPLIES,
            QUEST_ITEM,
            1,
            None,
        );
        (given, consumed)
    });
    assert_eq!(given, Some((true, 1)));

    let inv = crate::systems::economy::inventory::Inv::of(&a.p);
    assert_eq!(inv.items_count(QUEST_ITEM, -1), 2);
    assert_eq!(a.p.data().money, 250);
    let ops = a.client.drain_ops();
    let tasks = ops
        .iter()
        .filter(|o| **o == sc::SCItemTaskSuccessPacket)
        .count();
    assert_eq!(tasks, 3, "{ops:x?}");
}

#[tokio::test]
async fn item_skill_hooks_use_the_economy_inventory() {
    use crate::models::item_task::task_type;
    let server = server_with(false);
    let a = pc(&server, 1, "Crafter", 100.0);
    let items = server.systems.skills.ext.items();

    assert!(items.item_template_exists(&server, QUEST_ITEM));
    assert!(items.try_add_new_item(
        &server,
        &a.p,
        task_type::SKILL_EFFECT_GAIN_ITEM,
        QUEST_ITEM,
        4,
        0
    ));
    assert_eq!(items.item_count(&server, &a.p, QUEST_ITEM), 4);
    assert_eq!(
        items.consume_bag_item(&server, &a.p, task_type::SKILL_REAGENTS, QUEST_ITEM, 3),
        3
    );
    assert_eq!(items.item_count(&server, &a.p, QUEST_ITEM), 1);
    // No equipment: no gear stats, no holdable.
    assert_eq!(
        items.gear_stats(&server, &a.p),
        crate::systems::skills::ext::GearStats::default()
    );
    assert!(items
        .weapon_holdable(&server, &WorldObject::from(&a.p), 15)
        .is_none());
}

#[tokio::test]
async fn character_death_loses_exp_into_recoverable_exp() {
    use crate::systems::skills::types::KillReason;
    let server = server_with(false);
    let mut a = pc(&server, 1, "Victim", 100.0);
    let b = pc(&server, 2, "Killer", 102.0);
    {
        let mut c = a.p.data();
        c.unit.level = 10;
        c.experience = 95_000; // level 10 + 5 000
    }
    a.client.drain();

    crate::systems::skills::combat::do_die(
        &server,
        &WorldObject::from(&a.p),
        Some(&WorldObject::from(&b.p)),
        KillReason::Damage,
    );

    // GetExpLoss(10, 0.05) = floor(10 000 * 0.05) = 500, 80 % recoverable.
    assert_eq!(a.p.data().experience, 94_500);
    assert_eq!(a.p.data().recoverable_exp, 400);
    let ops = a.client.drain_ops();
    for op in [
        sc::SCRecoverableExpPacket,
        sc::SCExpChangedPacket,
        sc::SCUnitDeathPacket,
    ] {
        assert!(ops.contains(&op), "missing {op:#x} in {ops:x?}");
    }
}

// ------------------------------------------------------------------------------------------
// PvP deaths, resurrection, sticky parents, underwater (CharacterCombat / CSResurrectCharacter
// / CSHang / CSUnhang / Character.SetPosition)
// ------------------------------------------------------------------------------------------

const CONFLICT_GROUP: u16 = 14;

/// `server_with(false)` whose test zone belongs to a conflict zone group in `state`.
fn conflict_server(state: u8, num_kills: [i32; 5]) -> Arc<GameServer> {
    let mut gd = game_data();
    gd.zones.zones.insert(
        crate::test_support::TEST_ZONE,
        crate::data::world_data::Zone {
            id: 1,
            name: "test".into(),
            zone_key: crate::test_support::TEST_ZONE,
            group_id: u32::from(CONFLICT_GROUP),
            ..Default::default()
        },
    );
    let s = &mut gd.systems.skills;
    for id in [
        crate::systems::skills::resurrect::WEAKENED_BODY,
        crate::systems::skills::resurrect::RESPAWN_COOLDOWN,
        crate::systems::skills::resurrect::WAR_ZONE_LEECH,
    ] {
        s.add_buff(crate::systems::skills::data::templates::BuffTemplate {
            id,
            kind: 2,
            duration: 60_000,
            stack_rule: 1,
            ..Default::default()
        });
    }
    let server = test_server_with(gd);
    let server = Arc::new(server);
    server.set_self();
    crate::systems::skills::bind(&server);
    crate::systems::npcs::install_hooks(&server);
    crate::systems::social::install_hooks(&server);
    crate::systems::quests::install_hooks(&server);
    crate::systems::economy::wiring::install(&server);
    crate::systems::economy::bridges::install(&server);
    server.systems.social.conflicts.init(
        &[crate::data::world_data::ZoneConflict {
            zone_group_id: CONFLICT_GROUP,
            num_kills,
            conflict_min: 5,
            war_min: 80,
            peace_min: 120,
            current_state: state,
            ..Default::default()
        }],
        crate::world::now_ms(),
    );
    server
}

/// `pc` of an unknown faction (relation Neutral: a PvP kill, not friendly fire).
fn enemy_pc(server: &GameServer, id: u32, name: &str, x: f32) -> Pc {
    let pc = pc(server, id, name, x);
    pc.p.data().unit.faction_id = 9_999;
    pc
}

#[tokio::test]
async fn solo_pvp_kill_in_a_conflict_zone_awards_honor() {
    use crate::data::world_data::conflict_type;
    use crate::systems::skills::types::KillReason;
    let server = conflict_server(conflict_type::CONFLICT, [0; 5]);
    let mut victim = pc(&server, 1, "Victim", 100.0);
    let mut killer = enemy_pc(&server, 2, "Killer", 102.0);
    // Damage history of the victim is cleared on death.
    crate::systems::skills::combat::record_pvp_damage_from(&victim.p, killer.p.character_id);
    victim.client.drain();
    killer.client.drain();

    crate::systems::skills::combat::do_die(
        &server,
        &WorldObject::from(&victim.p),
        Some(&WorldObject::from(&killer.p)),
        KillReason::Damage,
    );

    {
        let k = killer.p.data();
        assert_eq!(k.hostile_faction_kills, 1);
        assert_eq!(k.honor_point, 10); // solo Conflict kill
        assert_eq!(k.honor_gained_in_combat, 10);
    }
    {
        let v = victim.p.data();
        assert!(v.died_in_pvp);
        assert!(!v.died_in_pvp_war_zone);
        assert!(v.skill_char.pvp_damage_history.is_empty());
        // ComputeDeathWaitTime: the first death waits 15 s.
        assert_eq!(v.rez_wait_duration, 15_000);
    }
    let ops = killer.client.drain_ops();
    assert_eq!(
        ops.iter()
            .filter(|o| **o == sc::SCUnitPvPPointsChangedPacket)
            .count(),
        2,
        "{ops:x?}"
    );
    assert!(ops.contains(&sc::SCConflictZoneHonorPointSumPacket));
    assert!(ops.contains(&sc::SCGamePointChangedPacket));
    // No honor change for the victim outside War.
    assert!(!victim
        .client
        .drain_ops()
        .contains(&sc::SCGamePointChangedPacket));
}

#[tokio::test]
async fn war_zone_kill_splits_honor_with_assists_and_costs_the_victim_honor() {
    use crate::data::world_data::conflict_type;
    use crate::systems::skills::types::KillReason;
    let server = conflict_server(conflict_type::WAR, [0; 5]);
    let victim = pc(&server, 1, "Victim", 100.0);
    let killer = enemy_pc(&server, 2, "Killer", 102.0);
    let helper = enemy_pc(&server, 3, "Helper", 104.0);
    victim.p.data().honor_point = 25;
    crate::systems::skills::combat::record_pvp_damage_from(&victim.p, helper.p.character_id);
    // A heal of the killer by the helper counts too (no duplicate assist).
    crate::systems::skills::combat::record_pvp_heal_from(&killer.p, helper.p.character_id);

    crate::systems::skills::combat::do_die(
        &server,
        &WorldObject::from(&victim.p),
        Some(&WorldObject::from(&killer.p)),
        KillReason::Damage,
    );

    assert_eq!(killer.p.data().honor_point, 16);
    assert_eq!(helper.p.data().honor_point, 4);
    assert_eq!(helper.p.data().honor_gained_in_combat, 4);
    assert_eq!(victim.p.data().honor_point, 15); // -10 in War
    let v = victim.p.data();
    assert!(v.died_in_pvp && v.died_in_pvp_war_zone);
}

#[tokio::test]
async fn kills_in_a_conflict_zone_do_not_move_the_kill_counter() {
    use crate::data::world_data::conflict_type;
    use crate::systems::skills::types::KillReason;
    // AddZoneKill ignores zones in Conflict or later.
    let server = conflict_server(conflict_type::CONFLICT, [1, 2, 3, 4, 5]);
    let victim = pc(&server, 1, "Victim", 100.0);
    let killer = enemy_pc(&server, 2, "Killer", 102.0);
    crate::systems::skills::combat::do_die(
        &server,
        &WorldObject::from(&victim.p),
        Some(&WorldObject::from(&killer.p)),
        KillReason::Damage,
    );
    let c = server
        .systems
        .social
        .conflicts
        .get(u32::from(CONFLICT_GROUP))
        .unwrap();
    assert_eq!(
        (c.current_state, c.kill_count),
        (conflict_type::CONFLICT, 0)
    );
}

#[tokio::test]
async fn death_wait_time_escalates_and_resets() {
    let server = test_server_with(game_data());
    let client = TestClient::new(&server, 1, 1001);
    let p = spawn_player(
        &server,
        &client,
        test_character(1, "A", 100.0, 100.0),
        0x101,
    );
    let t0 = 1_000_000_000_000;
    let waits: Vec<i32> = (0..11)
        .map(|i| {
            crate::systems::skills::combat::compute_death_wait_time(&p, t0 + i * 1000);
            p.data().rez_wait_duration
        })
        .collect();
    assert_eq!(
        waits,
        [15, 30, 60, 90, 120, 150, 180, 210, 240, 240, 240].map(|s| s * 1000)
    );
    assert_eq!(p.data().dead_time, (t0 + 10_000) / 1000);
    // 5 minutes without dying reset the counter.
    crate::systems::skills::combat::compute_death_wait_time(&p, t0 + 10_000 + 300_000);
    assert_eq!(p.data().rez_wait_duration, 15_000);
}

fn buff_ids(p: &PlayerRef) -> Vec<u32> {
    let mut v: Vec<u32> = p
        .data()
        .unit
        .buffs
        .effects
        .iter()
        .map(|b| b.template.id)
        .filter(|id| *id < 8_000_000) // not the test_support SCUnitState buffs
        .collect();
    v.sort_unstable();
    v
}

#[tokio::test]
async fn temple_resurrection_applies_the_death_debuffs() {
    use crate::systems::skills::resurrect::{
        resurrect_character, RESPAWN_COOLDOWN, WAR_ZONE_LEECH, WEAKENED_BODY,
    };
    let server = conflict_server(crate::data::world_data::conflict_type::PEACE, [0; 5]);
    let mut a = pc(&server, 1, "A", 100.0);
    let mut b = pc(&server, 2, "B", 102.0);

    // PvE death: Weakened Body + Respawn-CD, 10 % HP / MP, broadcast to the others too.
    a.p.data().unit.hp = 0;
    b.client.drain();
    a.client.drain();
    resurrect_character(&server, &a.p, false);
    let hp_mp = {
        let d = a.p.data();
        (d.unit.hp, d.unit.mp)
    };
    assert_eq!(hp_mp, (500, 100));
    assert_eq!(buff_ids(&a.p), [WEAKENED_BODY, RESPAWN_COOLDOWN]);
    let ops = a.client.drain_ops();
    assert!(ops.contains(&sc::SCCharacterResurrectedPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCUnitPointsPacket));
    assert!(b
        .client
        .drain_ops()
        .contains(&sc::SCCharacterResurrectedPacket));
    // Respawn-CD has the forced 5 minute duration.
    let cd =
        a.p.data()
            .unit
            .buffs
            .effects
            .iter()
            .find(|b| b.template.id == RESPAWN_COOLDOWN)
            .map(|b| b.duration);
    assert_eq!(cd, Some(300_000));

    // War-zone PvP death: Leech + Respawn-CD, flags cleared.
    let c = pc(&server, 3, "C", 104.0);
    {
        let mut d = c.p.data();
        d.unit.hp = 0;
        d.died_in_pvp = true;
        d.died_in_pvp_war_zone = true;
    }
    resurrect_character(&server, &c.p, false);
    assert_eq!(buff_ids(&c.p), [RESPAWN_COOLDOWN, WAR_ZONE_LEECH]);
    let flags = {
        let d = c.p.data();
        (d.died_in_pvp, d.died_in_pvp_war_zone)
    };
    assert_eq!(flags, (false, false));

    // Plain PvP death: Respawn-CD only.
    let d = pc(&server, 4, "D", 106.0);
    d.p.data().unit.hp = 0;
    d.p.data().died_in_pvp = true;
    resurrect_character(&server, &d.p, false);
    assert_eq!(buff_ids(&d.p), [RESPAWN_COOLDOWN]);
    assert!(!d.p.data().died_in_pvp);

    // In place (player resurrection): percent of max, no debuffs, percents reset to 1.
    let e = pc(&server, 5, "E", 108.0);
    {
        let mut x = e.p.data();
        x.unit.hp = 0;
        x.died_in_pvp = true;
        x.skill_char.resurrect_hp_percent = 50;
        x.skill_char.resurrect_mp_percent = 20;
    }
    resurrect_character(&server, &e.p, true);
    let x = e.p.data();
    assert_eq!((x.unit.hp, x.unit.mp), (2_500, 200));
    assert_eq!(
        (
            x.skill_char.resurrect_hp_percent,
            x.skill_char.resurrect_mp_percent
        ),
        (1, 1)
    );
    assert!(!x.died_in_pvp);
    drop(x);
    assert!(buff_ids(&e.p).is_empty());
}

#[tokio::test]
async fn clear_all_aggro_clears_the_assault_lists_both_ways() {
    let server = server_with(false);
    let a = pc(&server, 1, "A", 100.0);
    let b = pc(&server, 2, "B", 102.0);
    a.p.data().skill_char.assaulted_by.push(2);
    b.p.data().skill_char.assault_on.push(1);
    b.p.data().skill_char.assaulted_by.push(1);
    a.p.data().skill_char.assault_on.push(2);
    crate::systems::skills::combat::clear_all_aggro(&server, &WorldObject::from(&a.p));
    assert!(a.p.data().skill_char.assaulted_by.is_empty());
    assert!(a.p.data().skill_char.assault_on.is_empty());
    assert!(b.p.data().skill_char.assault_on.is_empty());
    assert!(b.p.data().skill_char.assaulted_by.is_empty());
}

#[tokio::test]
async fn hang_and_unhang_track_the_sticky_parent() {
    let server = server_with(false);
    let mut a = pc(&server, 1, "A", 100.0);
    let mut b = pc(&server, 2, "B", 102.0);
    let npc = villager(&server, 0x900, 100);
    npc.data().unit.scale = 1.0;
    let me = WorldObject::from(&a.p);
    a.client.drain();
    b.client.drain();

    crate::systems::vehicles::harpoon::hang(&server, &a.p, a.p.obj_id, npc.obj_id);
    let world = server.world.world(0).unwrap();
    assert_eq!(me.core().sticky_parent(), npc.obj_id);
    // StickyParent is also the parent (world position kept).
    assert_eq!(me.transform().parent_obj_id, npc.obj_id);
    let w = world.world_pos_rot(&me);
    assert!(
        (w.x - 100.0).abs() < 1e-3 && (w.y - 100.0).abs() < 1e-3,
        "{w:?} local {:?} parent scale {}",
        me.transform(),
        WorldObject::from(&npc).scale()
    );
    // SCHungPacket goes to the others only.
    assert!(b.client.drain_ops().contains(&sc::SCHungPacket));
    assert!(!a.client.drain_ops().contains(&sc::SCHungPacket));

    crate::systems::vehicles::harpoon::unhang(&server, &a.p, a.p.obj_id, 2);
    assert_eq!(me.core().sticky_parent(), 0);
    assert_eq!(me.transform().parent_obj_id, 0);
    let unhung: Vec<_> = b
        .client
        .drain()
        .into_iter()
        .filter(|(op, _)| *op == sc::SCUnhungPacket)
        .collect();
    assert_eq!(unhung.len(), 1);
    // bc unit, bc target (the former sticky parent), u32 reason
    let body = &unhung[0].1;
    assert_eq!(&body[3..6], &npc.obj_id.to_le_bytes()[..3]);
}

#[tokio::test]
async fn moving_under_the_water_surface_toggles_the_underwater_state() {
    let server = server_with(false);
    let mut a = pc(&server, 1, "A", 100.0);
    a.client.drain();
    let me = WorldObject::from(&a.p);
    // Ocean level 100: the surface band is 2 m, the exit hysteresis 0.35 m.
    crate::systems::vehicles::set_unit_position(&server, &me, 100.0, 100.0, 97.0, 0.0, 0.0, 0.0);
    assert!(a.p.data().unit.is_under_water);
    let sent: Vec<_> = a
        .client
        .drain()
        .into_iter()
        .filter(|(op, _)| *op == sc::SCUnderWaterPacket)
        .collect();
    assert_eq!(sent, vec![(sc::SCUnderWaterPacket, vec![1])]);
    a.p.data().breath = 10;
    // Inside the hysteresis band: still under water.
    crate::systems::vehicles::set_unit_position(&server, &me, 100.0, 100.0, 98.2, 0.0, 0.0, 0.0);
    assert!(a.p.data().unit.is_under_water);
    crate::systems::vehicles::set_unit_position(&server, &me, 100.0, 100.0, 98.5, 0.0, 0.0, 0.0);
    assert!(!a.p.data().unit.is_under_water);
    assert_eq!(a.p.data().breath, 60_000); // LungCapacity
    let sent: Vec<_> = a
        .client
        .drain()
        .into_iter()
        .filter(|(op, _)| *op == sc::SCUnderWaterPacket)
        .collect();
    assert_eq!(sent, vec![(sc::SCUnderWaterPacket, vec![0])]);

    // NPCs: below OceanLevel - 2.
    let npc = villager(&server, 0x901, 100);
    let n = WorldObject::from(&npc);
    crate::systems::vehicles::set_unit_position(&server, &n, 105.0, 100.0, 97.9, 0.0, 0.0, 0.0);
    assert!(npc.data().unit.is_under_water);
    crate::systems::vehicles::set_unit_position(&server, &n, 105.0, 100.0, 98.1, 0.0, 0.0, 0.0);
    assert!(!npc.data().unit.is_under_water);
}
