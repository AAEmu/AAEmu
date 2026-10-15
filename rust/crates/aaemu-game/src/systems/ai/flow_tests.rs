//! Flow tests of the AI subsystem without MySQL: an aggressive NPC sees a player, chases and
//! hits it with its base skill, and returns home (and resets) when the player runs away; the
//! state machine, aggro help, idle-to-default transitions, path following, the return timeout
//! teleport and the hooks used by the NPC / skills subsystems.

use std::sync::Arc;
use std::time::Duration;

use crate::data::GameData;
use crate::models::movement::GameStanceType;
use crate::models::npc::Npc;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::systems::skills::controllers::Vec3;
use crate::systems::skills::data::effects::{DamageEffect, EffectTemplate};
use crate::systems::skills::data::{BuffTemplate, EffectKind, SkillEffect, SkillTemplate};
use crate::systems::skills::state::AggroKind;
use crate::test_support::*;
use crate::world::{Entity, NpcRef, PlayerRef, WorldObject};

use super::data::{ActorModel, GameStance};
use super::framework::{AiKind, BehaviorKind as B};
use super::manager::{post, tick_all};
use super::path_handler::{AiPathPoint, AiPathPointAction};

const PLAYER_OBJ: u32 = 0x100;
const NPC_OBJ: u32 = 0x200;
const HELPER_OBJ: u32 = 0x201;
const NPC_TEMPLATE: u32 = 7000;
const PASSIVE_TEMPLATE: u32 = 7001;
const HELPER_TEMPLATE: u32 = 7002;
const MODEL: u32 = 900;
const HIT_SKILL: u32 = 10_001;
const HIT_DAMAGE: i32 = 40;

fn damage_effect(id: u32, amount: i32) -> DamageEffect {
    DamageEffect {
        id,
        damage_type: 1,
        fixed_min: amount,
        fixed_max: amount,
        multiplier: 1.0,
        use_mainhand_weapon: false,
        use_offhand_weapon: false,
        use_ranged_weapon: false,
        critical_bonus: 0,
        target_buff_tag_id: 0,
        target_buff_bonus: 0,
        use_fixed_damage: true,
        use_level_damage: false,
        level_md: 0.0,
        level_va_start: 0,
        level_va_end: 0,
        target_buff_bonus_mul: 1.0,
        use_charged_buff: false,
        charged_buff_id: 0,
        charged_mul: 0.0,
        aggro_multiplier: 1.0,
        health_steal_ratio: 0,
        mana_steal_ratio: 0,
        dps_multiplier: 0.0,
        weapon_slot_id: -1,
        check_crime: false,
        hit_anim_timing_id: 0,
        use_target_charged_buff: false,
        target_charged_buff_id: 0,
        target_charged_mul: 0.0,
        dps_inc_multiplier: 0.0,
        engage_combat: true,
        synergy: false,
        actability_group_id: 0,
        actability_step: 0,
        actability_mul: 0.0,
        actability_add: 0.0,
        charged_level_mul: 0.0,
        adjust_damage_by_height: false,
        use_percent_damage: false,
        percent_min: 0,
        percent_max: 0,
        use_current_health: false,
        target_health_min: 0,
        target_health_max: 0,
        target_health_mul: 0.0,
        target_health_add: 0,
        fire_proc: false,
        bonuses: Vec::new(),
    }
}

fn skill_effect(effect_id: u32, kind: EffectKind, actual: u32) -> SkillEffect {
    SkillEffect {
        effect_id,
        template: Some((kind, actual)),
        weight: 0,
        start_level: 0,
        end_level: 255,
        friendly: false,
        non_friendly: false,
        target_buff_tag_id: 0,
        target_no_buff_tag_id: 0,
        source_buff_tag_id: 0,
        source_no_buff_tag_id: 0,
        chance: 100,
        front: false,
        back: false,
        target_npc_tag_id: 0,
        application_method: 1,
        consume_source_item: false,
        consume_item_id: 0,
        consume_item_count: 0,
        always_hit: false,
        item_set_id: 0,
        interaction_success_hit: false,
    }
}

fn stance(
    stance_id: GameStanceType,
    walk: f32,
    run: f32,
    sprint: f32,
) -> (GameStanceType, GameStance) {
    (
        stance_id,
        GameStance {
            id: u32::from(stance_id.0) + 1,
            stance_id,
            actor_model_id: 1,
            ai_move_speed_run: run,
            ai_move_speed_slow: walk,
            ai_move_speed_sprint: sprint,
            ai_move_speed_walk: walk,
            height_collider: 0.0,
            height_pivot: 0.0,
            normal_speed: run,
            max_speed: 20.0,
            size: [0.0; 3],
            model_offset: [0.0; 3],
            view_offset: [0.0; 3],
            use_capsule: true,
        },
    )
}

/// An aggressive hold-position NPC template whose base skill hits for 40 within 4 m, a passive
/// helper template, the actor model with run / walk / sprint speeds, and the NpcReturn buff.
fn data() -> GameData {
    let mut data = GameData::empty();
    let s = &mut data.systems.skills;
    s.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(damage_effect(1, HIT_DAMAGE))),
        900_001,
    );
    let mut hit = SkillTemplate {
        id: HIT_SKILL,
        target_type: 4, // Hostile
        max_range: 4,
        level_rule_no_consideration: true,
        damage_type_id: 1,
        default_gcd: false,
        ..SkillTemplate::default()
    };
    hit.effects
        .push(skill_effect(900_001, EffectKind::DamageEffect, 1));
    s.add_skill(hit);
    s.add_buff(BuffTemplate {
        id: super::npc::BUFF_NPC_RETURN,
        kind: 1,
        duration: 0,
        stack_rule: 1,
        ..BuffTemplate::default()
    });
    let base = NpcTemplate {
        id: NPC_TEMPLATE,
        model_id: MODEL,
        level: 10,
        scale: 1.0,
        aggression: true,
        attack_start_range_scale: 2.0,
        sight_range_scale: 1.0,
        sight_fov_scale: 2.0,
        base_skill_id: HIT_SKILL as i32,
        base_skill_delay: 0.2,
        ai_file_id: 15, // HoldPosition
        accept_aggro_link: true,
        aggro_link_help_dist: 30.0,
        ..NpcTemplate::default()
    };
    // not aggressive, blind (no alerts)
    let passive = NpcTemplate {
        id: PASSIVE_TEMPLATE,
        aggression: false,
        sight_range_scale: 0.0,
        ..base.clone()
    };
    // aggressive (helps without a sight check) but with a 1 m aggression radius
    let helper = NpcTemplate {
        id: HELPER_TEMPLATE,
        attack_start_range_scale: 0.1,
        sight_range_scale: 0.0,
        ..base.clone()
    };
    data.systems.npcs.npc.templates.insert(NPC_TEMPLATE, base);
    data.systems
        .npcs
        .npc
        .templates
        .insert(PASSIVE_TEMPLATE, passive);
    data.systems
        .npcs
        .npc
        .templates
        .insert(HELPER_TEMPLATE, helper);
    // ModelManager.GetActorModel(MODEL).Radius as seen by the skills geometry
    data.systems.skills.model_radii.insert(MODEL, 0.5);
    data.systems.ai.ai.actor_models.insert(
        MODEL,
        Arc::new(ActorModel {
            id: 1,
            radius: 0.5,
            height: 2.0,
            movement_id: 0,
            underwater_creature: false,
            stances: vec![
                stance(GameStanceType::COMBAT, 2.0, 5.0, 8.0),
                stance(GameStanceType::RELAXED, 2.0, 5.0, 8.0),
            ],
        }),
    );
    data
}

struct Fixture {
    server: Arc<GameServer>,
    client: TestClient,
    player: PlayerRef,
    npc: NpcRef,
}

fn server() -> Arc<GameServer> {
    server_with_data(data())
}

/// [`data`]'s server setup (shared by [`server`]).
fn server_with_data(data: GameData) -> Arc<GameServer> {
    let server = Arc::new(test_server_with(data));
    server.set_self();
    crate::systems::skills::bind(&server);
    // The AI parts of the npcs NpcSkillHooks (`npcs::bridges::NpcSkills` at startup; its npc
    // stat overrides need npc data the bare test NPCs do not have).
    server.systems.skills.ext.npcs.install(Arc::new(AiParts));
    super::hooks::install(&server);
    server
}

/// [`data`] plus skill 2 (the melee hack's id) with max range 25; `PASSIVE_TEMPLATE` uses it
/// so the melee guard has to see the 3D distance of a flying NPC.
fn data_with_melee_skill() -> GameData {
    let mut data = data();
    let mut melee = data
        .systems
        .skills
        .skill(HIT_SKILL)
        .expect("hit skill")
        .as_ref()
        .clone();
    melee.id = 2;
    melee.max_range = 25;
    data.systems.skills.add_skill(melee);
    data.systems
        .npcs
        .npc
        .templates
        .get_mut(&PASSIVE_TEMPLATE)
        .expect("passive template")
        .base_skill_id = 2;
    data
}

/// Creates an NPC with an AI and spawns it like `NpcSpawnerNpc.Spawn` (home position, Spawning).
fn spawn_npc(
    server: &Arc<GameServer>,
    obj_id: u32,
    template: u32,
    x: f32,
    y: f32,
    kind: AiKind,
) -> NpcRef {
    let mut n = Npc::new(template);
    n.unit.name = "Wolf".into();
    n.unit.level = 10;
    n.unit.hp = 1000;
    n.unit.max_hp = 1000;
    n.unit.mp = 100;
    n.unit.max_mp = 100;
    n.unit.model_id = MODEL;
    n.unit.scale = 1.0;
    n.unit.transform.set_xyz(x, y, 100.0);
    n.unit.transform.zone_id = TEST_ZONE;
    let npc = Entity::new(obj_id, n);
    server.systems.ai.ais.add_ai(&npc, kind);
    let world = server.world.world(0).expect("world");
    world.spawn(&npc, &server.view_ctx());
    super::hooks::spawn_positioned(server, &npc);
    npc
}

fn fixture(player_x: f32) -> Fixture {
    let server = server();
    let client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Runner", player_x, 100.0);
    c.unit.hp = 1000;
    c.unit.max_hp = 1000;
    let player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        NPC_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    Fixture {
        server,
        client,
        player,
        npc,
    }
}

fn behavior(server: &GameServer, id: u32) -> Option<B> {
    server
        .systems
        .ai
        .ais
        .inspect(id, |a| a.current_behavior())
        .flatten()
}

fn npc_pos(npc: &NpcRef) -> Vec3 {
    let t = npc.data().unit.transform;
    Vec3::new(t.x, t.y, t.z)
}

/// Ticks the AI manager (`delta_ms` per tick, `sleep_ms` real time between ticks) until `done`.
async fn tick_until(
    server: &Arc<GameServer>,
    delta_ms: i64,
    sleep_ms: u64,
    max: usize,
    mut done: impl FnMut() -> bool,
) -> bool {
    for _ in 0..max {
        tick_all(server, delta_ms);
        if done() {
            return true;
        }
        tokio::time::sleep(Duration::from_millis(sleep_ms)).await;
    }
    false
}

/// Opcodes of the drained frames, with the level-4 (compressed) batches opened.
fn frame_ops(client: &mut TestClient) -> Vec<u16> {
    let mut out = Vec::new();
    for (op, body) in client.drain() {
        if op & 0x8000 != 0 && (op & 0x7FFF) != 0 {
            if let Some(raw) = crate::systems::skills::packets::inflate_stored(&body) {
                // each batched packet starts with [u16 0][u16 opcode]; the first one is enough
                // for the tests (the skill code batches one packet per frame here)
                if raw.len() >= 4 {
                    out.push(u16::from_le_bytes([raw[2], raw[3]]));
                }
                continue;
            }
        }
        out.push(op);
    }
    out
}

fn move_player(server: &GameServer, p: &PlayerRef, x: f32, y: f32) {
    p.data().unit.transform.set_xyz(x, y, 100.0);
    let world = server.world.world(0).expect("world");
    world.add_visible_object(p, &server.view_ctx());
}

#[tokio::test]
async fn aggressive_npc_chases_hits_and_returns_home() {
    let mut f = fixture(115.0);
    let server = f.server.clone();
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Spawning));
    // Spawning -> RunCommandSet -> Idle -> HoldPosition / Attack: the player 15 m away is
    // inside the 20 m aggression radius.
    let attacked = tick_until(&server, 100, 20, 50, || {
        behavior(&server, NPC_OBJ) == Some(B::Attack)
    })
    .await;
    assert!(
        attacked,
        "npc never attacked: {:?}",
        behavior(&server, NPC_OBJ)
    );
    {
        let n = f.npc.data();
        assert!(n.unit.is_in_battle);
        assert!(n.unit.skill_state.aggro_table.contains_key(&PLAYER_OBJ));
        assert_eq!(n.current_game_stance, GameStanceType::COMBAT);
    }
    f.client.drain();

    // Chase (run speed 5 m/s, 0.5 s per tick) and hit with the base skill.
    let start = npc_pos(&f.npc);
    let player = f.player.clone();
    let hit = tick_until(&server, 500, 160, 80, || player.data().unit.hp < 1000).await;
    assert!(hit, "player never damaged; npc at {:?}", npc_pos(&f.npc));
    let now = npc_pos(&f.npc);
    assert!(
        now.x > start.x + 9.0,
        "npc did not chase: {start:?} -> {now:?}"
    );
    assert_eq!((1000 - f.player.data().unit.hp) % HIT_DAMAGE, 0);
    let ops = frame_ops(&mut f.client);
    assert!(ops.contains(&sc::SCOneUnitMovementPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCTargetChangedPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCUnitDamagedPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCAggroTargetChanged), "{ops:x?}");
    assert_eq!(f.npc.data().unit.current_target, PLAYER_OBJ);
    assert_eq!(f.npc.data().current_aggro_target, PLAYER_OBJ);

    // Hurt the NPC a little: ReturnState restores it.
    f.npc.data().unit.hp = 700;

    // The player runs 70 m away (beyond ReturnDistance 50 from the NPC, still in its region
    // neighbourhood): the NPC gives up, returns home at sprint speed and resets.
    move_player(&server, &f.player, 185.0, 100.0);
    let npc = f.npc.clone();
    let returned = tick_until(&server, 500, 30, 80, || {
        behavior(&server, NPC_OBJ) == Some(B::HoldPosition)
    })
    .await;
    assert!(
        returned,
        "npc did not return: {:?} at {:?}",
        behavior(&server, NPC_OBJ),
        npc_pos(&npc)
    );
    let home = npc_pos(&f.npc);
    assert!(
        (home.x - 100.0).abs() < 1.0 && (home.y - 100.0).abs() < 1.0,
        "{home:?}"
    );
    let n = f.npc.data();
    assert!(n.unit.skill_state.aggro_table.is_empty());
    assert!(!n.unit.is_in_battle);
    assert_eq!(n.unit.hp, 1000, "ReturnState restores the hp");
    assert_eq!(
        n.current_aggro_target, PLAYER_OBJ,
        "CurrentAggroTarget is never reset by ReturnState"
    );
    assert_eq!(
        n.unit.current_target, NPC_OBJ,
        "HoldPosition targets itself"
    );
    assert!(
        !n.unit.buffs.check_buff(super::npc::BUFF_NPC_RETURN),
        "NpcReturn buff removed on exit"
    );
    assert_eq!(n.current_game_stance, GameStanceType::RELAXED);
    drop(n);
    // the player keeps the damage, and is no longer in this NPC's aggro list
    assert!(!f
        .player
        .data()
        .skill_char
        .is_in_aggro_list_of
        .contains(&NPC_OBJ));
}

#[tokio::test]
async fn passive_npc_fights_back_and_calls_for_help() {
    let server = server();
    let client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Hunter", 103.0, 100.0);
    c.unit.hp = 1000;
    c.unit.max_hp = 1000;
    let player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::Roaming,
    );
    let helper = spawn_npc(
        &server,
        HELPER_OBJ,
        HELPER_TEMPLATE,
        95.0,
        100.0,
        AiKind::Default,
    );
    // settle both into their default behaviors (Roaming / DoNothing): no aggression
    tick_until(&server, 100, 120, 10, || false).await;
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Roaming));
    assert_eq!(behavior(&server, HELPER_OBJ), Some(B::DoNothing));
    assert!(npc.data().unit.skill_state.aggro_table.is_empty());

    // Npc.OnDamageReceived: aggro + OnAggroTargetChanged (Roaming -> Attack)
    let attacker = WorldObject::from(&player);
    super::combat::add_unit_aggro(
        &server,
        &WorldObject::from(&npc),
        AggroKind::Damage,
        &attacker,
        50,
    );
    server
        .systems
        .skills
        .ext
        .npcs()
        .npc_damage_received(&server, &npc, &attacker, 50);
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Attack));
    // UpdateTarget + UpdateAggroHelp: the helper (accepts aggro links, within 30 m) joins
    tick_all(&server, 100);
    assert_eq!(npc.data().unit.current_target, PLAYER_OBJ);
    assert_eq!(behavior(&server, HELPER_OBJ), Some(B::Attack));
    assert!(helper
        .data()
        .unit
        .skill_state
        .aggro_table
        .contains_key(&PLAYER_OBJ));
}

#[tokio::test]
async fn ai_state_machine_events_and_go_tos() {
    let server = server();
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    // GoToSpawn happened through the hook
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Spawning));
    // No transition for an event the behavior does not handle
    post(&server, NPC_OBJ, |c| c.on_no_aggro_target());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Spawning));
    // Spawning -> RunCommandSet -> Idle -> HoldPosition (default) without players around
    // (no player activity: nothing ticks, so drive the behaviors directly)
    post(&server, NPC_OBJ, |c| c.go_to_idle());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Idle));
    assert!(npc.data().ai_hold_or_idle);
    post(&server, NPC_OBJ, |c| c.go_to_default_behavior());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::HoldPosition));
    assert_eq!(npc.data().unit.current_target, NPC_OBJ);
    // OnAggroTargetChanged from HoldPosition -> Attack, OnNoAggroTarget from Attack -> Return
    post(&server, NPC_OBJ, |c| c.on_aggro_target_changed());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Attack));
    assert!(npc.data().unit.is_in_battle);
    post(&server, NPC_OBJ, |c| c.on_no_aggro_target());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::ReturnState));
    assert!(!npc.data().unit.is_in_battle);
    assert!(npc
        .data()
        .unit
        .buffs
        .check_buff(super::npc::BUFF_NPC_RETURN));
    // GoToDead from anywhere
    post(&server, NPC_OBJ, |c| c.go_to_dead());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Dead));
    assert!(!npc
        .data()
        .unit
        .buffs
        .check_buff(super::npc::BUFF_NPC_RETURN));
    // a behavior the character lacks is ignored
    post(&server, NPC_OBJ, |c| c.go_to_dummy());
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Dead));
}

#[tokio::test]
async fn return_state_teleports_home_after_the_timeout() {
    let server = server();
    let client = TestClient::new(&server, 1, 1);
    let c = test_character(1, "Watcher", 150.0, 100.0);
    let _player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    // displaced 30 m from home and rooted in place by a 0 move speed model stance
    npc.data().unit.transform.set_xyz(130.0, 100.0, 100.0);
    post(&server, NPC_OBJ, |c| {
        c.go_to_return();
        // expire the 20 s timeout
        c.st(B::ReturnState).timeout_time = 0;
        // MoveTowards does nothing while displaced
        c.npc.data().unit.displaced_until_ms = Some(i64::MAX);
    });
    tick_all(&server, 100);
    // OnCompletedReturn: farther than 4 m -> MoveTowards(idle, 1000000) is blocked by the
    // displacement too, then GoToIdle
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Idle));
    npc.data().unit.displaced_until_ms = None;
    post(&server, NPC_OBJ, |c| {
        c.go_to_return();
        c.st(B::ReturnState).timeout_time = 0;
    });
    tick_all(&server, 100);
    let p = npc_pos(&npc);
    assert!((p.x - 100.0).abs() < 1.5, "teleported home: {p:?}");
}

#[tokio::test]
async fn follow_path_moves_along_the_points() {
    let server = server();
    let client = TestClient::new(&server, 1, 1);
    let c = test_character(1, "Watcher", 300.0, 300.0);
    let _player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::Default,
    );
    server.systems.ai.paths.insert(
        "test_path",
        vec![
            AiPathPoint {
                position: Vec3::new(0.0, 0.0, 0.0),
                action: AiPathPointAction::Speed,
                param: "4".into(),
            },
            AiPathPoint {
                position: Vec3::new(110.0, 100.0, 100.0),
                action: AiPathPointAction::None,
                param: String::new(),
            },
            AiPathPoint {
                position: Vec3::new(110.0, 110.0, 100.0),
                action: AiPathPointAction::DisableLoop,
                param: String::new(),
            },
        ],
    );
    super::hooks::spawned(&server, &npc, "test_path");
    // the loaded path makes the AI persistent (ticks without players around)
    assert!(server
        .systems
        .ai
        .ais
        .inspect(NPC_OBJ, |a| a.has_persistent_ai())
        .unwrap());
    // Spawning -> RunCommandSet -> Idle -> FollowPath (CheckFollowPath)
    let following = tick_until(&server, 100, 110, 20, || {
        behavior(&server, NPC_OBJ) == Some(B::FollowPath)
    })
    .await;
    assert!(following, "{:?}", behavior(&server, NPC_OBJ));
    // 4 m/s * 0.5 s per tick
    let reached = tick_until(&server, 500, 5, 40, || {
        let p = npc_pos(&npc);
        (p.x - 110.0).abs() < 1.1 && (p.y - 110.0).abs() < 1.1
    })
    .await;
    assert!(reached, "{:?}", npc_pos(&npc));
    // path done (loop disabled): back to Idle / default
    tick_until(&server, 100, 110, 6, || false).await;
    assert_ne!(behavior(&server, NPC_OBJ), Some(B::FollowPath));
}

#[tokio::test]
async fn hooks_create_ai_by_file_type_and_detach_on_delete() {
    let server = server();
    let mk = |id: u32| Entity::new(id, Npc::new(PASSIVE_TEMPLATE));
    let a = mk(0x300);
    assert!(super::hooks::create_ai(&server, &a, 22));
    assert!(a.data().has_ai);
    assert_eq!(
        server.systems.ai.ais.inspect(0x300, |ai| ai.kind),
        Some(AiKind::ArcherRoaming)
    );
    // SiegeWeaponPlace / Bomb / undefined types have no AI (GetAiByType returns null)
    for (id, t) in [(0x301, 11), (0x302, 12), (0x303, 14), (0x304, 0)] {
        let n = mk(id);
        n.data().has_ai = true;
        assert!(!super::hooks::create_ai(&server, &n, t));
        assert!(!n.data().has_ai);
    }
    // Npc.Delete: a spawned NPC that left the world is detached on the next tick
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    tick_all(&server, 100);
    assert!(super::manager::has_ai(&server, NPC_OBJ));
    server.world.world(0).unwrap().delete(&npc);
    tick_all(&server, 100);
    assert!(!super::manager::has_ai(&server, NPC_OBJ));
}

#[tokio::test]
async fn npc_death_goes_to_dead_and_clears_aggro() {
    let server = server();
    let client = TestClient::new(&server, 1, 1);
    let c = test_character(1, "Killer", 102.0, 100.0);
    let player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    let p = WorldObject::from(&player);
    super::combat::add_unit_aggro(&server, &WorldObject::from(&npc), AggroKind::Damage, &p, 10);
    post(&server, NPC_OBJ, |c| c.on_aggro_target_changed());
    assert!(player
        .data()
        .skill_char
        .is_in_aggro_list_of
        .contains(&NPC_OBJ));
    // Unit.DoDie -> NpcSkillHooks.npc_after_death (the AI decorator)
    npc.data().unit.hp = 0;
    crate::systems::skills::combat::do_die(
        &server,
        &WorldObject::from(&npc),
        Some(&p),
        crate::systems::skills::types::KillReason::Damage,
    );
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::Dead));
    assert!(npc.data().unit.skill_state.aggro_table.is_empty());
    assert!(player.data().skill_char.is_in_aggro_list_of.is_empty());
    assert!(!player.data().unit.is_in_battle);
}

/// The AI calls of `systems::npcs::bridges::NpcSkills` (`Npc.OnDamageReceived`,
/// `OnNoAggroTarget`, `CheckIfEmptyAggroToReturn`, `Npc.DoDie` -> `GoToDead`, the
/// `NpcControlEffect`, the move state).
struct AiParts;

impl crate::systems::skills::ext::NpcSkillHooks for AiParts {
    fn npc_after_death(
        &self,
        server: &GameServer,
        npc: &NpcRef,
        _killer: Option<&crate::world::WorldObject>,
    ) {
        super::hooks::on_npc_died(server, npc);
    }

    fn npc_damage_received(
        &self,
        server: &GameServer,
        npc: &NpcRef,
        _attacker: &crate::world::WorldObject,
        _amount: i32,
    ) {
        super::hooks::on_damage_received(server, npc);
    }

    fn npc_no_aggro_target(&self, server: &GameServer, npc: &NpcRef) {
        super::hooks::on_no_aggro_target(server, npc);
    }

    fn npc_aggro_removed(&self, server: &GameServer, _npc: &NpcRef, abuser: u32) {
        super::hooks::check_abuser_empty_aggro(server, abuser);
    }

    fn npc_move_state(
        &self,
        _server: &GameServer,
        npc: &NpcRef,
    ) -> (
        crate::models::movement::GameStanceType,
        crate::models::movement::MoveTypeAlertness,
    ) {
        let n = npc.data();
        (n.current_game_stance, n.current_alertness)
    }

    fn apply_effect(
        &self,
        server: &GameServer,
        _kind: crate::systems::skills::data::EffectKind,
        template: &crate::systems::skills::data::EffectTemplate,
        args: &crate::systems::skills::effects::EffectArgs<'_>,
    ) -> bool {
        if let crate::systems::skills::data::EffectTemplate::NpcControl(e) = template {
            if let Some(n) = args
                .target
                .as_obj()
                .and_then(crate::world::WorldObject::as_npc)
            {
                super::hooks::npc_control_effect(server, e, n);
            }
            return true;
        }
        false
    }
}

/// `Npc.Delete()`: the AI is detached and forgotten with the NPC.
#[tokio::test]
async fn npc_delete_detaches_its_ai() {
    let f = fixture(300.0);
    assert!(f.server.systems.ai.ais.get(NPC_OBJ).is_some());
    let world = f.server.world.world(0).expect("world");
    crate::systems::npcs::npc::manager::delete(&f.server, &world, &f.npc);
    assert!(f.server.systems.ai.ais.get(NPC_OBJ).is_none());
    assert!(!f.npc.data().has_ai);
    assert!(world.npc(NPC_OBJ).is_none());
}

#[tokio::test]
async fn npc_deleting_itself_from_an_ai_call_does_not_stall_other_enemies() {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::mpsc;

    let f = fixture(115.0);
    let server = f.server.clone();
    let queued_ran = Arc::new(AtomicBool::new(false));
    let queued = queued_ran.clone();
    let (finished, completion) = mpsc::channel();
    let worker = std::thread::spawn(move || {
        // NPC skills such as KillNpcWithoutCorpse can delete their caster during
        // an AI step. Callbacks already queued for that AI must be discarded.
        post(&server, NPC_OBJ, move |c| {
            post(c.server, NPC_OBJ, move |_| {
                queued.store(true, Ordering::SeqCst);
            });
            let world = c.server.world.world(0).expect("world");
            crate::systems::npcs::npc::manager::delete(c.server, &world, &c.npc);
        });
        finished.send(()).unwrap();
    });
    completion
        .recv_timeout(Duration::from_secs(2))
        .expect("NPC deletion deadlocked inside its own AI call");
    worker.join().unwrap();
    assert!(!queued_ran.load(Ordering::SeqCst));
    assert!(!super::manager::has_ai(&f.server, NPC_OBJ));
    assert!(f.server.world.world(0).unwrap().npc(NPC_OBJ).is_none());

    spawn_npc(
        &f.server,
        HELPER_OBJ,
        NPC_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    let player = f.player.clone();
    assert!(
        tick_until(&f.server, 500, 160, 60, || player.data().unit.hp < 1000).await,
        "another enemy did not chase and attack after NPC deletion"
    );
}

#[tokio::test]
async fn busy_npc_ai_does_not_stop_other_enemies_chasing_and_attacking() {
    use std::sync::mpsc;

    let f = fixture(115.0);
    // The busy NPC stays registered in the AI manager (the global tick still has to reach
    // it), but is parked outside the healthy enemy's neighbourhood: without faction
    // templates `can_attack` is true for the collocated NPC, and the enemy would attack it
    // instead of chasing the player.
    {
        let npc = &f.npc;
        npc.data().unit.transform.set_xyz(1000.0, 1000.0, 100.0);
    }
    let world = f.server.world.world(0).expect("world");
    world.add_visible_object(&f.npc, &f.server.view_ctx());
    let other = spawn_npc(
        &f.server,
        HELPER_OBJ,
        NPC_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    let (entered, ready) = mpsc::channel();
    let (release, resume) = mpsc::channel();
    let server = f.server.clone();
    let holder = std::thread::spawn(move || {
        post(&server, NPC_OBJ, move |_| {
            entered.send(()).unwrap();
            resume.recv().unwrap();
        });
    });
    ready.recv_timeout(Duration::from_secs(2)).unwrap();

    let server = f.server.clone();
    let runtime = tokio::runtime::Handle::current();
    let (finished, completion) = mpsc::channel();
    let ticker = std::thread::spawn(move || {
        let _runtime = runtime.enter();
        for _ in 0..10 {
            tick_all(&server, 500);
            std::thread::sleep(Duration::from_millis(160));
        }
        finished.send(()).unwrap();
    });
    let result = completion.recv_timeout(Duration::from_secs(5));
    // Always release the holder, including when checking the old blocking tick.
    release.send(()).unwrap();
    holder.join().unwrap();
    ticker.join().unwrap();
    result.expect("one busy NPC blocked the global AI tick");
    assert!(npc_pos(&other).x > 100.0, "another enemy stopped moving");
    assert!(
        f.player.data().unit.hp < 1000,
        "another enemy stopped attacking"
    );
}

/// A damaged flying NPC descends onto its attacker in 3D and, once the aggro is gone, climbs
/// back to its spawn altitude; the return must not end at the home XY while still below it.
#[tokio::test]
async fn flying_npc_descends_to_its_attacker_and_returns_to_spawn_altitude() {
    // The ranged base skill (max 4) and the melee-id skill (id 2, max 25, built on the same
    // effect, but with the 4 m guard) in both geodata modes.
    for (geo_data_mode, melee_skill) in [(false, false), (true, false), (false, true), (true, true)]
    {
        let data = if melee_skill {
            data_with_melee_skill()
        } else {
            data()
        };
        let server = server_with_data(data);
        server
            .config
            .update(|cfg| cfg.world.geo_data_mode = geo_data_mode);
        let client = TestClient::new(&server, 1, 1);
        let mut c = test_character(1, "Hunter", 100.0, 100.0);
        // Survive the base skill for the whole descent.
        c.unit.hp = 100_000;
        c.unit.max_hp = 100_000;
        let player = spawn_player(&server, &client, c, PLAYER_OBJ);
        // The attacker stands straight below the flying NPC's spawn position.
        player.data().unit.transform.set_xyz(100.0, 100.0, 80.0);
        let world = server.world.world(0).expect("world");
        world.add_visible_object(&player, &server.view_ctx());

        let npc = spawn_npc(
            &server,
            NPC_OBJ,
            PASSIVE_TEMPLATE,
            100.0,
            100.0,
            AiKind::HoldPosition,
        );
        npc.data().can_fly = true;
        // Settle to HoldPosition before the damage aggro (like the passive-help pattern).
        let settled = tick_until(&server, 100, 20, 10, || {
            behavior(&server, NPC_OBJ) == Some(B::HoldPosition)
        })
        .await;
        assert!(
            settled,
            "never settled: {:?} (geo_data_mode={geo_data_mode})",
            behavior(&server, NPC_OBJ)
        );
        assert_eq!(npc_pos(&npc), Vec3::new(100.0, 100.0, 100.0));
        assert_eq!(npc.data().current_game_stance, GameStanceType::FLY);

        // Npc.OnDamageReceived, as passive_npc_fights_back_and_calls_for_help.
        let attacker = WorldObject::from(&player);
        super::combat::add_unit_aggro(
            &server,
            &WorldObject::from(&npc),
            AggroKind::Damage,
            &attacker,
            50,
        );
        server
            .systems
            .skills
            .ext
            .npcs()
            .npc_damage_received(&server, &npc, &attacker, 50);
        assert_eq!(behavior(&server, NPC_OBJ), Some(B::Attack));

        // Bounded descent: 0.5 m per 500 ms tick (no FLY stance in the model, so
        // BaseMoveSpeed falls back to 1) until the 2 m attack range; edge distance subtracts
        // the 0.5 m model radius, so the raw 3D distance stops at 2.5 m.
        let mut in_range = false;
        let mut last_z = 100.0;
        for tick in 0..80 {
            tick_all(&server, 500);
            // Let async skill casts and their strafe gates finish between simulated ticks.
            tokio::time::sleep(Duration::from_millis(2)).await;
            let (current_target, aggro_target, stance, pos) = {
                let n = npc.data();
                let t = n.unit.transform;
                (
                    n.unit.current_target,
                    n.current_aggro_target,
                    n.current_game_stance,
                    Vec3::new(t.x, t.y, t.z),
                )
            };
            assert_eq!(
                current_target, PLAYER_OBJ,
                "lost the attacker at tick {tick}: {pos:?} (geo_data_mode={geo_data_mode})"
            );
            assert_eq!(aggro_target, PLAYER_OBJ, "aggro lost at tick {tick}");
            assert_eq!(
                stance,
                GameStanceType::FLY,
                "stance left FLY at tick {tick}"
            );
            assert!(
                pos.z <= 100.0 && pos.z > 80.0,
                "flew past the attacker at tick {tick}: {pos:?}"
            );
            assert!(
                (last_z - pos.z) <= 0.5 + 0.001 && pos.z <= last_z + 0.001,
                "unbounded descent step at tick {tick}: {last_z} -> {pos:?}"
            );
            last_z = pos.z;
            // Outside the 4 m 3D edge distance (raw distance minus the 0.5 m model radius):
            // neither the height-aware skill range nor the melee skill-2 guard may let the
            // flyer cast. With the old 2D caller distance the melee fixture hits from here.
            if pos.z - 80.0 > 4.5 + 0.001 {
                assert_eq!(
                    player.data().unit.hp,
                    100_000,
                    "cast at a victim out of 3D range at tick {tick}: {pos:?} \
                     (geo_data_mode={geo_data_mode}, melee_skill={melee_skill})"
                );
            }
            if pos.z <= 82.5 {
                in_range = true;
                break;
            }
        }
        assert!(
            in_range,
            "never descended into attack range: {:?} (geo_data_mode={geo_data_mode})",
            npc_pos(&npc)
        );
        assert!(npc
            .data()
            .unit
            .skill_state
            .aggro_table
            .contains_key(&PLAYER_OBJ));

        // Inside the 4 m 3D range the base skill reaches the attacker (real sleep lets the
        // real-time skill interval and any async cast complete).
        let damaged = tick_until(&server, 500, 20, 30, || player.data().unit.hp < 100_000).await;
        assert!(
            damaged,
            "never hit the attacker after closing range: {:?} (geo_data_mode={geo_data_mode})",
            npc_pos(&npc)
        );

        // Losing aggro (the player stays for AI activity): the flyer climbs straight up while
        // still in ReturnState and only leaves it within MoveTowards's 1 m arrival tolerance
        // of the home z.
        post(&server, NPC_OBJ, |c| c.on_no_aggro_target());
        assert_eq!(behavior(&server, NPC_OBJ), Some(B::ReturnState));
        assert_eq!(npc.data().current_game_stance, GameStanceType::FLY);
        let mut zs = Vec::new();
        let mut left_return = false;
        for _ in 0..80 {
            tick_all(&server, 500);
            tokio::time::sleep(Duration::from_millis(2)).await;
            if behavior(&server, NPC_OBJ) != Some(B::ReturnState) {
                left_return = true;
                break;
            }
            zs.push(npc_pos(&npc).z);
        }
        assert!(
            left_return,
            "stuck in ReturnState at {:?} (geo_data_mode={geo_data_mode})",
            npc_pos(&npc)
        );
        assert!(zs.len() > 2, "no gradual climb: {zs:?}");
        assert!(
            zs.windows(2).all(|w| w[1] >= w[0]),
            "descended during the return: {zs:?} (geo_data_mode={geo_data_mode})"
        );
        assert!(
            zs.first() < zs.last(),
            "did not climb back: {zs:?} (geo_data_mode={geo_data_mode})"
        );
        assert!(
            zs.iter().all(|z| *z <= 100.0 + 0.001),
            "climbed past home: {zs:?}"
        );
        assert!(
            (zs.last().copied().unwrap_or(0.0) - 100.0).abs() <= 1.0 + 0.001,
            "left ReturnState outside the arrival tolerance: {zs:?}"
        );
        let home = npc_pos(&npc);
        assert!(
            (home.x - 100.0).abs() < 0.01 && (home.y - 100.0).abs() < 0.01,
            "return drifted in XY: {home:?} (geo_data_mode={geo_data_mode})"
        );
        assert!(
            (home.z - 100.0).abs() <= 1.0 + 0.001,
            "return stopped outside the arrival tolerance: {home:?} (geo_data_mode={geo_data_mode})"
        );
    }
}

/// The ReturnState timeout teleport must count vertical displacement: the same XY as home but
/// 20 m below is not home for a flyer.
#[tokio::test]
async fn flying_npc_timeout_return_counts_vertical_displacement() {
    let server = server();
    // The player keeps the NPC's region active so `tick_all` advances its AI.
    let client = TestClient::new(&server, 1, 1);
    let c = test_character(1, "Watcher", 150.0, 100.0);
    let _player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let npc = spawn_npc(
        &server,
        NPC_OBJ,
        PASSIVE_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    npc.data().can_fly = true;
    let settled = tick_until(&server, 100, 20, 10, || {
        behavior(&server, NPC_OBJ) == Some(B::HoldPosition)
    })
    .await;
    assert!(settled, "{:?}", behavior(&server, NPC_OBJ));

    npc.data().unit.transform.set_xyz(100.0, 100.0, 80.0);
    post(&server, NPC_OBJ, |c| {
        c.go_to_return();
        c.st(B::ReturnState).timeout_time = 0;
    });
    assert_eq!(behavior(&server, NPC_OBJ), Some(B::ReturnState));
    tick_all(&server, 500);
    let home = npc_pos(&npc);
    assert!(
        (home.z - 100.0).abs() < 0.01,
        "timeout did not restore the home altitude: {home:?}"
    );
    assert!(
        (home.x - 100.0).abs() < 0.01 && (home.y - 100.0).abs() < 0.01,
        "{home:?}"
    );
}

/// A guard NPC that kills a hostile mob must drop it as target, stop attacking and leave combat
/// (live report: Graymist guards kept attacking the dead mob, whose client health bar stayed
/// gray as if the death never registered).
const MOB_OBJ: u32 = 0x202;
const GUARD2_OBJ: u32 = 0x203;
const GUARD_FACTION: u32 = 101;
const MOB_FACTION: u32 = 109;

/// [`data`] with the guard faction (also the test characters' faction), hostile to the mob faction.
fn guard_data() -> GameData {
    use crate::data::world_data::{FactionRelation, SystemFaction};
    let mut data = data();
    for id in [GUARD_FACTION, MOB_FACTION] {
        data.factions.add(SystemFaction {
            id,
            ..SystemFaction::default()
        });
    }
    data.factions.relations.push(FactionRelation {
        id: GUARD_FACTION,
        id2: MOB_FACTION,
        state: crate::systems::skills::types::RelationState::Hostile as u8,
        exp_time: 0,
    });
    data
}

fn tracked_state(server: &GameServer, guard: &NpcRef, mob: &NpcRef) -> String {
    let (target, aggro_target, battle, aggro) = {
        let g = guard.data();
        let mut keys: Vec<u32> = g.unit.skill_state.aggro_table.keys().copied().collect();
        keys.sort_unstable();
        (
            g.unit.current_target,
            g.current_aggro_target,
            g.unit.is_in_battle,
            keys,
        )
    };
    let (hp, dead) = {
        let m = mob.data();
        (m.unit.hp, m.unit.is_dead())
    };
    format!(
        "guard[beh={:?} target={target:#x} aggro_target={aggro_target:#x} battle={battle} aggro={aggro:x?}] mob[hp={hp} dead={dead} beh={:?}]",
        behavior(server, NPC_OBJ),
        behavior(server, MOB_OBJ),
    )
}

/// `with_player`: a same-faction bystander 30 m away (region activity + a packet receiver);
/// without one the guard is seeded with aggro on the mob (an idle NPC without region activity is
/// dormant, so it would never notice the mob by itself).
async fn run_guard_kills_mob(guard_kind: AiKind, with_player: bool, second_guard: bool) {
    let server = server_with_data(guard_data());
    let mut client = TestClient::new(&server, 1, 1);
    let player = with_player.then(|| {
        let mut c = test_character(1, "Bystander", 130.0, 100.0);
        c.unit.hp = 1000;
        c.unit.max_hp = 1000;
        spawn_player(&server, &client, c, PLAYER_OBJ)
    });
    let guard = spawn_npc(&server, NPC_OBJ, NPC_TEMPLATE, 100.0, 100.0, guard_kind);
    guard.data().unit.faction_id = GUARD_FACTION;
    let mob = spawn_npc(
        &server,
        MOB_OBJ,
        PASSIVE_TEMPLATE,
        103.0,
        100.0,
        AiKind::Default,
    );
    mob.data().unit.faction_id = MOB_FACTION;
    mob.data().unit.hp = HIT_DAMAGE + 20;
    mob.data().unit.max_hp = HIT_DAMAGE + 20;
    let guard2 = second_guard.then(|| {
        let g = spawn_npc(
            &server,
            GUARD2_OBJ,
            NPC_TEMPLATE,
            100.0,
            103.0,
            AiKind::HoldPosition,
        );
        g.data().unit.faction_id = GUARD_FACTION;
        g
    });
    if !with_player {
        // settle (the spawn / command-set grace runs without region activity), then seed
        tick_until(&server, 100, 120, 5, || false).await;
        let mob_obj = WorldObject::from(&mob);
        super::combat::add_unit_aggro(
            &server,
            &WorldObject::from(&guard),
            AggroKind::Damage,
            &mob_obj,
            1,
        );
        post(&server, NPC_OBJ, |c| c.on_aggro_target_changed());
    }
    client.drain();

    let mut trace = Vec::new();
    let mut died_at = None;
    for i in 0..120 {
        tick_all(&server, 500);
        tokio::time::sleep(Duration::from_millis(160)).await;
        let ops = frame_ops(&mut client);
        let line = format!(
            "#{i:03} {} ops={:x?}",
            tracked_state(&server, &guard, &mob),
            ops
        );
        if died_at.is_none() && mob.data().unit.hp <= 0 {
            died_at = Some(i);
        }
        trace.push((i, line, ops));
        if died_at.is_some_and(|d| i >= d + 30) {
            break;
        }
    }
    for (_, line, _) in &trace {
        eprintln!("{line}");
    }
    let died = died_at.expect("the guard never killed the mob");
    assert!(mob.data().unit.is_dead(), "hp 0 but the mob is not dead");
    assert_eq!(behavior(&server, MOB_OBJ), Some(B::Dead));
    // the death packet reached the bystander, after the points packet
    if with_player {
        let ops = &trace[died].2;
        let death = ops.iter().position(|o| *o == sc::SCUnitDeathPacket);
        assert!(
            death.is_some(),
            "no SCUnitDeathPacket to the player: {ops:x?}"
        );
        let points = ops.iter().position(|o| *o == sc::SCUnitPointsPacket);
        assert!(points < death, "points must precede death: {ops:x?}");
    }
    // after the death: no further skill starts / damage against the dead mob
    for (i, line, ops) in &trace[(died + 3).min(trace.len() - 1)..] {
        assert!(
            !ops.contains(&sc::SCUnitDamagedPacket),
            "guard still attacking the dead mob at tick {i}: {line}"
        );
    }
    // the dead mob is no target / aggro entry anywhere and the guards left combat
    for g in [Some(&guard), guard2.as_ref()].into_iter().flatten() {
        let gd = g.data();
        assert!(
            !gd.unit.skill_state.aggro_table.contains_key(&MOB_OBJ),
            "dead mob still in aggro table: {}",
            tracked_state(&server, g, &mob)
        );
        assert_ne!(gd.unit.current_target, MOB_OBJ, "dead mob still targeted");
        assert!(!gd.unit.is_in_battle, "guard still in battle");
    }
    assert_ne!(behavior(&server, NPC_OBJ), Some(B::Attack));
    drop(player);
}

#[tokio::test]
async fn guard_stops_attacking_a_mob_it_killed() {
    run_guard_kills_mob(AiKind::HoldPosition, true, false).await;
}

#[tokio::test]
async fn two_guards_stop_attacking_a_mob_they_killed() {
    run_guard_kills_mob(AiKind::HoldPosition, true, true).await;
}

/// The kill lands while the mob's AI is busy on another thread (a player's skill task / the
/// blocking-pool AI batch racing the guard): `go_to_dead` is queued behind the damage effect's
/// tail, which re-adds the killer to the dead mob's aggro table
/// (`skills/effects/combat.rs` damage: `add_unit_aggro(trg, ..)` after `reduce_current_hp`).
/// `dead_enter` -> `npc_clear_all_aggro` then sees a non-empty table and, with the mob away from
/// its idle position, `check_if_empty_aggro_to_return` runs `go_to_return` on the corpse:
/// `return_state_enter` restores the hp, so the mob is alive again and hostile.
#[tokio::test]
async fn guard_kill_while_the_mob_ai_is_busy_keeps_the_mob_dead() {
    let server = server_with_data(guard_data());
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Bystander", 130.0, 100.0);
    c.unit.hp = 1000;
    c.unit.max_hp = 1000;
    let _player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let guard = spawn_npc(
        &server,
        NPC_OBJ,
        NPC_TEMPLATE,
        100.0,
        100.0,
        AiKind::HoldPosition,
    );
    guard.data().unit.faction_id = GUARD_FACTION;
    let mob = spawn_npc(
        &server,
        MOB_OBJ,
        PASSIVE_TEMPLATE,
        103.0,
        100.0,
        AiKind::Roaming,
    );
    mob.data().unit.faction_id = MOB_FACTION;
    // three hits: 40 + 40 + 40 >= 3 * 40 - 20
    mob.data().unit.hp = 3 * HIT_DAMAGE - 20;
    mob.data().unit.max_hp = 3 * HIT_DAMAGE - 20;
    client.drain();

    // normal engagement until the first hit landed
    let first_hit = tick_until(&server, 500, 160, 60, || mob.data().unit.hp < 100).await;
    assert!(first_hit, "{}", tracked_state(&server, &guard, &mob));
    // the mob chased / drifted away from its idle position
    mob.data().unit.transform.set_xyz(110.0, 100.0, 100.0);

    // block the mob AI (as a concurrent tick would) until it is dead
    let (release, wait) = std::sync::mpsc::channel::<()>();
    let (ready_tx, ready) = std::sync::mpsc::channel::<()>();
    let s = server.clone();
    let blocker = std::thread::spawn(move || {
        super::manager::run_now(&s, MOB_OBJ, move |_c| {
            ready_tx.send(()).expect("ready");
            let _ = wait.recv();
        });
    });
    ready.recv().expect("mob ai busy");
    let killed = tick_until(&server, 500, 160, 60, || mob.data().unit.hp <= 0).await;
    eprintln!(
        "after kill (mob ai still busy): mob hp {} guard aggro {:?}",
        mob.data().unit.hp,
        guard
            .data()
            .unit
            .skill_state
            .aggro_table
            .keys()
            .collect::<Vec<_>>()
    );
    eprintln!(
        "mob aggro table after kill: {:?}",
        mob.data()
            .unit
            .skill_state
            .aggro_table
            .keys()
            .collect::<Vec<_>>()
    );
    release.send(()).expect("release");
    blocker.join().expect("blocker");
    assert!(killed, "{}", tracked_state(&server, &guard, &mob));
    // the queued `go_to_dead` ran now: the corpse must stay a corpse
    let after_release = tracked_state(&server, &guard, &mob);
    eprintln!("after the queued AI calls ran: {after_release}");
    assert_eq!(
        mob.data().unit.hp,
        0,
        "the corpse was healed back to life by ReturnState: {after_release}"
    );
    // the death of the kill above
    let kill_ops = frame_ops(&mut client);
    assert_eq!(
        kill_ops
            .iter()
            .filter(|o| **o == sc::SCUnitDeathPacket)
            .count(),
        1,
        "{kill_ops:x?}"
    );
    let mut deaths = 0;
    for i in 0..40 {
        tick_all(&server, 500);
        tokio::time::sleep(Duration::from_millis(160)).await;
        let ops = frame_ops(&mut client);
        deaths += ops.iter().filter(|o| **o == sc::SCUnitDeathPacket).count();
        let line = tracked_state(&server, &guard, &mob);
        eprintln!("#{i:02} {line} ops={ops:x?}");
        assert_eq!(mob.data().unit.hp, 0, "the mob came back to life: {line}");
    }
    assert_eq!(deaths, 0, "the mob had to be killed a second time");
    assert_eq!(behavior(&server, MOB_OBJ), Some(B::Dead));
    assert!(!guard
        .data()
        .unit
        .skill_state
        .aggro_table
        .contains_key(&MOB_OBJ));
    assert_ne!(behavior(&server, NPC_OBJ), Some(B::Attack));
}
