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
//!   too (skills tagging -> npcs death -> social teams -> quests).

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
