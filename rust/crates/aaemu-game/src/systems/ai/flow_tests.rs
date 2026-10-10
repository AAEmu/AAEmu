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
    let server = Arc::new(test_server_with(data()));
    server.set_self();
    crate::systems::skills::bind(&server);
    super::hooks::install(&server);
    server
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
