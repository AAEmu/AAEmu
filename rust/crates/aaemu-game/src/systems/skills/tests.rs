//! Tests of the skills subsystem: a player casting a damage skill on an NPC through
//! `CSStartSkillPacket` (damage, death, packets), buff stacking / duration / bonuses / expiry,
//! heal and mana effects, plot execution, unit requirements.

use std::sync::Arc;
use std::time::Duration;

use aaemu_commons::PacketStream;

use crate::data::GameData;
use crate::models::buff::effect_state;
use crate::models::npc::Npc;
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::test_support::*;
use crate::world::{Entity, NpcRef, PlayerRef, WorldObject};

use super::data::effects::{DamageEffect, EffectTemplate, HealEffect, RestoreManaEffect};
use super::data::plots::{Plot, PlotEventEffect, PlotEventTemplate, PlotNode, PlotTree};
use super::data::{BuffTemplate, EffectKind, SkillEffect, SkillTemplate};
use super::handlers::{start_skill, CSStartSkillPacket};
use super::packets::{inflate_stored, LEVEL_COMPRESSED};
use super::types::{SkillCastTarget, SkillCaster, SkillObject};

const PLAYER_OBJ: u32 = 0x100;
const NPC_OBJ: u32 = 0x200;
const DAMAGE_SKILL: u32 = 10_001;
const HEAL_SKILL: u32 = 10_002;
const PLOT_SKILL: u32 = 10_003;
const BUFF_ID: u32 = 50_001;

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

fn skill_template(id: u32) -> SkillTemplate {
    SkillTemplate {
        id,
        target_type: 4, // Hostile
        max_range: 30,
        level_rule_no_consideration: true,
        damage_type_id: 1,
        default_gcd: false,
        ..SkillTemplate::default()
    }
}

/// Game data with a 600-damage skill, a heal / mana skill, a plot skill and a stat buff.
fn data() -> GameData {
    let mut data = GameData::empty();
    let s = &mut data.systems.skills;
    s.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(damage_effect(1, 600))),
        900_001,
    );
    s.add_effect(
        EffectKind::HealEffect,
        EffectTemplate::Heal(HealEffect {
            id: 2,
            use_fixed_heal: true,
            fixed_min: 150,
            fixed_max: 150,
            use_level_heal: false,
            level_md: 0.0,
            level_va_start: 0,
            level_va_end: 0,
            percent: false,
            use_charged_buff: false,
            charged_buff_id: 0,
            charged_mul: 0.0,
            slave_applicable: false,
            ignore_heal_aggro: false,
            dps_multiplier: 0.0,
            actability_group_id: 0,
            actability_step: 0,
            actability_mul: 0.0,
            actability_add: 0.0,
        }),
        900_002,
    );
    s.add_effect(
        EffectKind::RestoreManaEffect,
        EffectTemplate::RestoreMana(RestoreManaEffect {
            id: 3,
            use_fixed_value: true,
            fixed_min: 40,
            fixed_max: 40,
            use_level_value: false,
            level_md: 0.0,
            level_va_start: 0,
            level_va_end: 0,
            percent: false,
        }),
        900_003,
    );
    s.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(damage_effect(4, 100))),
        900_004,
    );
    let mut dmg = skill_template(DAMAGE_SKILL);
    dmg.effects
        .push(skill_effect(900_001, EffectKind::DamageEffect, 1));
    s.add_skill(dmg);
    let mut heal = skill_template(HEAL_SKILL);
    heal.target_type = 0; // Self
    heal.damage_type_id = 0;
    heal.effects
        .push(skill_effect(900_002, EffectKind::HealEffect, 2));
    heal.effects
        .push(skill_effect(900_003, EffectKind::RestoreManaEffect, 3));
    s.add_skill(heal);
    // plot skill: one event on the original target with a 100 damage effect
    let mut plot_skill = skill_template(PLOT_SKILL);
    plot_skill.plot_id = Some(77);
    plot_skill.plot_only = true;
    s.add_skill(plot_skill);
    s.plots.plots.insert(
        77,
        Plot {
            id: 77,
            target_type_id: 0,
            event_id: Some(700),
        },
    );
    s.plots.events.insert(
        700,
        PlotEventTemplate {
            id: 700,
            plot_id: 77,
            position: 1,
            source_update_method_id: 1,
            target_update_method_id: 2,
            tickets: 1,
            effects: vec![PlotEventEffect {
                position: 1,
                source_id: 1,
                target_id: 4,
                actual_id: 4,
                actual_type: "DamageEffect".into(),
            }],
            ..PlotEventTemplate::default()
        },
    );
    s.plots.trees.insert(
        77,
        PlotTree {
            plot_id: 77,
            nodes: vec![PlotNode {
                event_id: 700,
                parent: None,
                parent_next_event: None,
                children: Vec::new(),
            }],
        },
    );
    s.add_buff(BuffTemplate {
        id: BUFF_ID,
        kind: 1,
        duration: 60_000,
        stack_rule: 1,
        bonuses: vec![BonusTemplate {
            attribute: UnitAttribute::MaxHealth,
            modifier_type: UnitModifierType::Value,
            value: 100,
            linear_level_bonus: 0,
        }],
        ..BuffTemplate::default()
    });
    data
}

struct Fixture {
    server: Arc<GameServer>,
    client: TestClient,
    player: PlayerRef,
    npc: NpcRef,
}

fn fixture() -> Fixture {
    let server = Arc::new(test_server_with(data()));
    super::bind(&server);
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Caster", 100.0, 100.0);
    c.unit.hp = 500;
    c.unit.max_hp = 1000;
    c.unit.mp = 100;
    c.unit.max_mp = 1000;
    let player = spawn_player(&server, &client, c, PLAYER_OBJ);
    client.conn.active_char = Some(player.clone());
    let mut n = Npc::new(1234);
    n.unit.name = "Dummy".into();
    n.unit.level = 5;
    n.unit.hp = 1000;
    n.unit.max_hp = 1000;
    n.unit.transform.set_xyz(105.0, 100.0, 100.0);
    let npc = Entity::new(NPC_OBJ, n);
    let world = server.world.world(0).expect("world");
    world.spawn(&npc, &server.view_ctx());
    client.drain();
    Fixture {
        server,
        client,
        player,
        npc,
    }
}

fn cast_packet(skill_id: u32, target: SkillCastTarget) -> CSStartSkillPacket {
    CSStartSkillPacket {
        skill_id,
        caster: SkillCaster::Unit(PLAYER_OBJ),
        target,
        flag: 0,
        skill_object: SkillObject::None,
    }
}

/// Frames of a test client: level-1 (op, body) plus the inflated contents of level-4 batches.
fn frames(client: &mut TestClient) -> Vec<(u16, Vec<u8>)> {
    let mut out = Vec::new();
    for (op, body) in client.drain() {
        if op & 0x8000 != 0 && (op & 0x7FFF) != 0 {
            // level-4 frames come back as 0x8000 | count with the deflated payload as body
            if let Some(raw) = inflate_stored(&body) {
                let mut s = PacketStream::from_bytes(raw);
                for _ in 0..(op & 0x7FFF) {
                    let _zero = s.read_u16().expect("pad");
                    let inner = s.read_u16().expect("op");
                    let start = s.pos();
                    // the inner bodies are not length prefixed: take the rest for the last
                    // one, else find the next header (tests only batch one packet).
                    let rest = s.unread().to_vec();
                    s.set_pos(start + rest.len());
                    out.push((inner, rest));
                }
                continue;
            }
        }
        out.push((op, body));
    }
    out
}

#[tokio::test]
async fn player_kills_npc_with_damage_skill() {
    let mut f = fixture();
    let server = f.server.clone();
    let _ = LEVEL_COMPRESSED;

    // First cast: 600 damage.
    start_skill(
        &server,
        &mut f.client.conn,
        cast_packet(DAMAGE_SKILL, SkillCastTarget::Unit(NPC_OBJ)),
    );
    assert_eq!(f.npc.data().unit.hp, 400);
    let ops: Vec<u16> = frames(&mut f.client).into_iter().map(|f| f.0).collect();
    assert!(ops.contains(&sc::SCSkillFiredPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCUnitPointsPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCUnitDamagedPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCSkillEndedPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCCombatFirstHitPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCAiAggroPacket), "{ops:x?}");
    // The NPC is in combat, has the player in its aggro table and tagged it.
    {
        let n = f.npc.data();
        assert!(n.unit.is_in_battle);
        assert_eq!(
            n.unit
                .skill_state
                .aggro_table
                .get(&PLAYER_OBJ)
                .map(|a| a.damage_aggro),
            Some(600)
        );
        assert_eq!(n.unit.skill_state.tagging.tagger, PLAYER_OBJ);
    }
    assert!(f.player.data().unit.is_in_battle);

    // Second cast kills it (cooldown 0, gcd bypassed by the 150 ms interval rule only).
    f.player.data().unit.skill_last_used_ms = 0;
    start_skill(
        &server,
        &mut f.client.conn,
        cast_packet(DAMAGE_SKILL, SkillCastTarget::Unit(NPC_OBJ)),
    );
    assert_eq!(f.npc.data().unit.hp, 0);
    let fr = frames(&mut f.client);
    let death = fr
        .iter()
        .find(|f| f.0 == sc::SCUnitDeathPacket)
        .expect("death packet");
    let mut e = PacketStream::new();
    e.write_bc(NPC_OBJ)
        .write_u8(1) // KillReason.Damage
        .write_i32(0)
        .write_i32(0)
        .write_u8(0)
        .write_bc(PLAYER_OBJ)
        .write_u8(0)
        .write_u16(0)
        .write_u8(0)
        .write_u8(0)
        .write_u8(0)
        .write_str("Caster");
    assert_eq!(death.1, e.into_bytes());
    // dead NPC: points show 0 hp / 0 mp, out of combat
    let n = f.npc.data();
    assert!(n.unit.is_dead());
    assert!(!n.unit.is_in_battle);
    assert!(n.dead_time_ms > 0);
}

#[tokio::test]
async fn too_far_target_sends_skill_started_with_error() {
    let mut f = fixture();
    let server = f.server.clone();
    f.npc.data().unit.transform.set_xyz(160.0, 100.0, 100.0);
    let world = server.world.world(0).expect("world");
    world.add_visible_object(&f.npc, &server.view_ctx());
    f.client.drain();
    start_skill(
        &server,
        &mut f.client.conn,
        cast_packet(DAMAGE_SKILL, SkillCastTarget::Unit(NPC_OBJ)),
    );
    assert_eq!(f.npc.data().unit.hp, 1000);
    let fr = frames(&mut f.client);
    assert_eq!(fr.len(), 1, "{fr:x?}");
    assert_eq!(fr[0].0, sc::SCSkillStartedPacket);
    let mut e = PacketStream::new();
    e.write_u32(DAMAGE_SKILL)
        .write_u16(0)
        .write_u8(0)
        .write_bc(PLAYER_OBJ)
        .write_u8(0)
        .write_bc(NPC_OBJ)
        .write_u8(0)
        .write_u16(0)
        .write_u16(0)
        .write_u8(0)
        .write_u8(1) // ExtraDataFlags.HasByte
        .write_u8(0x0F); // TooFarRange
    assert_eq!(fr[0].1, e.into_bytes());
    // the TlId was released
    assert!(!server.systems.skills.tl_ids.is_assigned(0x0A));
}

#[tokio::test]
async fn heal_and_restore_mana_on_self() {
    let mut f = fixture();
    let server = f.server.clone();
    start_skill(
        &server,
        &mut f.client.conn,
        cast_packet(HEAL_SKILL, SkillCastTarget::Unit(PLAYER_OBJ)),
    );
    let c = f.player.data();
    // IncomingHealMul / HealMul of a formula-less character are (0 + 1000) / 1000 = 1
    assert_eq!(c.unit.hp, 650);
    assert_eq!(c.unit.mp, 140);
    drop(c);
    let ops: Vec<u16> = frames(&mut f.client).into_iter().map(|f| f.0).collect();
    assert_eq!(
        ops.iter().filter(|o| **o == sc::SCUnitHealedPacket).count(),
        2,
        "{ops:x?}"
    );
}

#[tokio::test]
async fn buff_bonuses_stacking_and_removal() {
    let f = fixture();
    let server = f.server.clone();
    let me = WorldObject::from(&f.player);
    let mut client = f.client;
    let t = server
        .game_data
        .systems
        .skills
        .buff(BUFF_ID)
        .cloned()
        .expect("buff");
    let b = super::buffs::new_buff(
        &server,
        &t,
        Some(&me),
        &SkillCaster::Unit(PLAYER_OBJ),
        None,
        crate::world::now_ms(),
    );
    super::buffs::add_buff(&server, &me, b, 0, 0);
    // Start: +100 MaxHealth (formula-less character: MaxHp = CalculateWithBonuses(0) = 100)
    assert_eq!(f.player.data().unit.max_hp, 100);
    let (index, state, duration) = {
        let c = f.player.data();
        let e = c
            .unit
            .buffs
            .effect_from_buff_id(BUFF_ID)
            .expect("effect")
            .clone();
        (e.index, e.state, e.duration)
    };
    assert_eq!(state, effect_state::ACTING);
    assert_eq!(duration, 60_000);
    let fr = frames(&mut client);
    let created = fr
        .iter()
        .find(|f| f.0 == sc::SCBuffCreatedPacket)
        .expect("buff created");
    let mut e = PacketStream::new();
    e.write_u8(0)
        .write_bc(PLAYER_OBJ)
        .write_u32(1) // Caster.Id (character id)
        .write_bc(PLAYER_OBJ)
        .write_u32(index)
        .write_u32(BUFF_ID)
        .write_u8(10)
        .write_i16(1)
        .write_u32(0)
        .write_pisc(&[0, 6000, 0, 0]);
    assert_eq!(created.1, e.into_bytes());

    // Refresh stack rule: a second application keeps one effect with the same index.
    let b = super::buffs::new_buff(
        &server,
        &t,
        Some(&me),
        &SkillCaster::Unit(PLAYER_OBJ),
        None,
        crate::world::now_ms(),
    );
    super::buffs::add_buff(&server, &me, b, 0, 0);
    assert_eq!(f.player.data().unit.buffs.buff_count_by_id(BUFF_ID), 1);
    assert_eq!(f.player.data().unit.max_hp, 100);
    client.drain();

    // Exit: bonuses removed, SCBuffRemovedPacket once.
    super::buffs::exit(&server, &me, index, false);
    assert!(!f.player.data().unit.buffs.check_buff(BUFF_ID));
    assert_eq!(f.player.data().unit.max_hp, 0);
    let fr = frames(&mut client);
    assert_eq!(
        fr.iter().filter(|f| f.0 == sc::SCBuffRemovedPacket).count(),
        1,
        "{fr:x?}"
    );

    // RemoveBuff (bookkeeping path) dispels twice, as the C# does.
    let b = super::buffs::new_buff(
        &server,
        &t,
        Some(&me),
        &SkillCaster::Unit(PLAYER_OBJ),
        None,
        crate::world::now_ms(),
    );
    super::buffs::add_buff(&server, &me, b, 0, 0);
    client.drain();
    super::buffs::remove_buff(&server, &me, BUFF_ID);
    let fr = frames(&mut client);
    assert_eq!(
        fr.iter().filter(|f| f.0 == sc::SCBuffRemovedPacket).count(),
        2,
        "{fr:x?}"
    );
    assert_eq!(f.player.data().unit.max_hp, 0);
}

#[tokio::test]
async fn timed_buff_expires_by_itself() {
    let mut data = data();
    data.systems.skills.add_buff(BuffTemplate {
        id: BUFF_ID + 1,
        kind: 2,
        duration: 50,
        ..BuffTemplate::default()
    });
    let server = Arc::new(test_server_with(data));
    super::bind(&server);
    let mut n = Npc::new(1);
    n.unit.hp = 10;
    n.unit.max_hp = 10;
    let npc = Entity::new(NPC_OBJ, n);
    server
        .world
        .world(0)
        .expect("world")
        .spawn(&npc, &server.view_ctx());
    let obj = WorldObject::from(&npc);
    super::buffs::add_buff_by_id(&server, &obj, BUFF_ID + 1, &obj, 0);
    assert!(npc.data().unit.buffs.check_buff(BUFF_ID + 1));
    tokio::time::sleep(Duration::from_millis(300)).await;
    assert!(!npc.data().unit.buffs.check_buff(BUFF_ID + 1));
}

#[tokio::test]
async fn plot_skill_runs_its_tree() {
    let mut f = fixture();
    let server = f.server.clone();
    start_skill(
        &server,
        &mut f.client.conn,
        cast_packet(PLOT_SKILL, SkillCastTarget::Unit(NPC_OBJ)),
    );
    // The plot runs on its own task.
    for _ in 0..50 {
        if f.npc.data().unit.hp != 1000 {
            break;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    tokio::time::sleep(Duration::from_millis(50)).await;
    assert_eq!(f.npc.data().unit.hp, 900);
    let ops: Vec<u16> = frames(&mut f.client).into_iter().map(|f| f.0).collect();
    assert!(ops.contains(&sc::SCUnitDamagedPacket), "{ops:x?}");
    assert!(ops.contains(&sc::SCPlotEndedPacket), "{ops:x?}");
    // the plot released its TlId and cleared ActivePlotState
    assert!(f.player.data().unit.skill_state.active_plot.is_none());
}

#[tokio::test]
async fn npc_skill_use_hits_player() {
    let f = fixture();
    let server = f.server.clone();
    let npc = WorldObject::from(&f.npc);
    let me = WorldObject::from(&f.player);
    let r = super::skill::unit_use_skill(&server, &npc, DAMAGE_SKILL, &me);
    assert_eq!(r, super::types::skill_result::SUCCESS);
    // 500 - 600 -> dead
    assert_eq!(f.player.data().unit.hp, 0);
}

// ------------------------------------------------------------------ combat rules

const COOLDOWN_SKILL: u32 = 10_004;
const LEVEL_REQ_SKILL: u32 = 10_005;
const ABSORB_BUFF: u32 = 50_002;
const FRAGILE_BUFF: u32 = 50_003;

fn rules_data() -> GameData {
    let mut data = data();
    let s = &mut data.systems.skills;
    let mut cd = skill_template(COOLDOWN_SKILL);
    cd.cooldown_time = 5000;
    cd.effects
        .push(skill_effect(900_004, EffectKind::DamageEffect, 4));
    s.add_skill(cd);
    let mut lvl = skill_template(LEVEL_REQ_SKILL);
    lvl.effects
        .push(skill_effect(900_004, EffectKind::DamageEffect, 4));
    s.add_skill(lvl);
    s.unit_reqs.insert(
        "Skill".into(),
        vec![super::data::UnitReq {
            id: 1,
            owner_id: LEVEL_REQ_SKILL,
            owner_type: "Skill".into(),
            kind: 1, // Level
            value1: 20,
            value2: 0,
        }],
    );
    s.add_buff(BuffTemplate {
        id: ABSORB_BUFF,
        kind: 1,
        duration: 60_000,
        damage_absorption_type_id: 1,
        init_min_charge: 250,
        init_max_charge: 250,
        ..BuffTemplate::default()
    });
    s.add_buff(BuffTemplate {
        id: FRAGILE_BUFF,
        kind: 1,
        duration: 60_000,
        remove_on_damaged_etc: true,
        ..BuffTemplate::default()
    });
    data
}

fn rules_fixture() -> Fixture {
    let server = Arc::new(test_server_with(rules_data()));
    super::bind(&server);
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Caster", 100.0, 100.0);
    c.unit.hp = 5000;
    c.unit.max_hp = 5000;
    let player = spawn_player(&server, &client, c, PLAYER_OBJ);
    client.conn.active_char = Some(player.clone());
    let mut n = Npc::new(1234);
    n.unit.hp = 1000;
    n.unit.max_hp = 1000;
    n.unit.transform.set_xyz(105.0, 100.0, 100.0);
    let npc = Entity::new(NPC_OBJ, n);
    server
        .world
        .world(0)
        .expect("world")
        .spawn(&npc, &server.view_ctx());
    client.drain();
    Fixture {
        server,
        client,
        player,
        npc,
    }
}

fn use_on_npc(f: &Fixture, skill_id: u32) -> (u8, u32) {
    let t = f
        .server
        .game_data
        .systems
        .skills
        .skill(skill_id)
        .cloned()
        .expect("skill");
    let skill = super::state::SkillRun::new(t, 1);
    let mut target = SkillCastTarget::Unit(NPC_OBJ);
    super::skill::use_skill(
        &f.server,
        &skill,
        &WorldObject::from(&f.player),
        SkillCaster::Unit(PLAYER_OBJ),
        &mut target,
        None,
        true,
    )
}

#[tokio::test]
async fn cooldowns_block_the_second_use() {
    let f = rules_fixture();
    assert_eq!(
        use_on_npc(&f, COOLDOWN_SKILL).0,
        super::types::skill_result::SUCCESS
    );
    assert_eq!(f.npc.data().unit.hp, 900);
    assert_eq!(
        use_on_npc(&f, COOLDOWN_SKILL).0,
        super::types::skill_result::COOLDOWN_TIME
    );
    assert_eq!(f.npc.data().unit.hp, 900);
}

#[tokio::test]
async fn unit_requirements_reject_low_level() {
    let f = rules_fixture();
    // level 10 < 20: skill_urk_level -> SkillResult 64
    assert_eq!(use_on_npc(&f, LEVEL_REQ_SKILL), (64, 0));
    f.player.data().unit.level = 20;
    assert_eq!(
        use_on_npc(&f, LEVEL_REQ_SKILL).0,
        super::types::skill_result::SUCCESS
    );
}

#[tokio::test]
async fn armor_reduces_and_absorption_consumes_charges() {
    let f = rules_fixture();
    let server = f.server.clone();
    let me = WorldObject::from(&f.player);
    let npc = WorldObject::from(&f.npc);
    // +5300 armor: reduction 1 - 5300 / 10600 = 0.5
    f.player.data().unit.bonuses.add_bonus(
        900,
        crate::models::unit::Bonus {
            template: BonusTemplate {
                attribute: UnitAttribute::Armor,
                modifier_type: UnitModifierType::Value,
                value: 5300,
                linear_level_bonus: 0,
            },
            value: 5300,
        },
    );
    assert_eq!(
        super::skill::unit_use_skill(&server, &npc, DAMAGE_SKILL, &me),
        0
    );
    assert_eq!(f.player.data().unit.hp, 5000 - 300);
    // absorption buff with 250 charge: 300 -> 50, the buff ends
    super::buffs::add_buff_by_id(&server, &me, ABSORB_BUFF, &me, 0);
    assert_eq!(
        super::combat::buff_of(&me, ABSORB_BUFF).map(|b| b.charge),
        Some(250)
    );
    f.npc.data().unit.skill_last_used_ms = 0;
    assert_eq!(
        super::skill::unit_use_skill(&server, &npc, DAMAGE_SKILL, &me),
        0
    );
    assert_eq!(f.player.data().unit.hp, 4700 - 50);
    assert!(!f.player.data().unit.buffs.check_buff(ABSORB_BUFF));
}

#[tokio::test]
async fn damage_removes_remove_on_damaged_buffs() {
    let f = rules_fixture();
    let server = f.server.clone();
    let npc = WorldObject::from(&f.npc);
    let me = WorldObject::from(&f.player);
    super::buffs::add_buff_by_id(&server, &npc, FRAGILE_BUFF, &npc, 0);
    assert!(npc.with_base_unit(|b| b.buffs().check_buff(FRAGILE_BUFF)));
    assert_eq!(use_on_npc(&f, COOLDOWN_SKILL).0, 0);
    assert!(!npc.with_base_unit(|b| b.buffs().check_buff(FRAGILE_BUFF)));
    // the player's own buffs are untouched by the attack (RemoveOnAttackEtc not set)
    super::buffs::add_buff_by_id(&server, &me, FRAGILE_BUFF, &me, 0);
    assert!(me.with_base_unit(|b| b.buffs().check_buff(FRAGILE_BUFF)));
}

#[tokio::test]
async fn healing_an_abuser_generates_heal_aggro() {
    let f = rules_fixture();
    let server = f.server.clone();
    let me = WorldObject::from(&f.player);
    let npc = WorldObject::from(&f.npc);
    super::combat::add_unit_aggro(&server, &npc, super::state::AggroKind::Damage, &me, 100);
    super::combat::on_abuser_healed(&server, &me, &me, 1000);
    let a = f
        .npc
        .data()
        .unit
        .skill_state
        .aggro_table
        .get(&PLAYER_OBJ)
        .copied()
        .expect("aggro");
    assert_eq!((a.damage_aggro, a.heal_aggro), (100, 600));
    // the abuser dying removes it from the table
    super::combat::do_die(&server, &me, Some(&npc), super::types::KillReason::Damage);
    assert!(f.npc.data().unit.skill_state.aggro_table.is_empty());
}

#[tokio::test]
async fn regen_tick_uses_regen_bonuses() {
    let f = rules_fixture();
    let server = f.server.clone();
    {
        let mut c = f.player.data();
        c.unit.hp = 100;
        c.unit.bonuses.add_bonus(
            901,
            crate::models::unit::Bonus {
                template: BonusTemplate {
                    attribute: UnitAttribute::MaxHealth,
                    modifier_type: UnitModifierType::Value,
                    value: 5000,
                    linear_level_bonus: 0,
                },
                value: 5000,
            },
        );
        c.unit.bonuses.add_bonus(
            901,
            crate::models::unit::Bonus {
                template: BonusTemplate {
                    attribute: UnitAttribute::HealthRegen,
                    modifier_type: UnitModifierType::Value,
                    value: 25,
                    linear_level_bonus: 0,
                },
                value: 25,
            },
        );
    }
    super::regen::active_region_tick(&server);
    assert_eq!(f.player.data().unit.hp, 125);
    // in combat the persistent regen (0 here) applies
    f.player.data().unit.is_in_battle = true;
    f.player.data().unit.last_combat_activity_ms = crate::world::now_ms();
    super::regen::active_region_tick(&server);
    assert_eq!(f.player.data().unit.hp, 125);
}

#[tokio::test]
async fn moving_removes_remove_on_move_buffs() {
    let mut data = data();
    data.systems.skills.add_buff(BuffTemplate {
        id: BUFF_ID + 2,
        kind: 2,
        duration: 60_000,
        remove_on_move: true,
        ..BuffTemplate::default()
    });
    let server = Arc::new(test_server_with(data));
    super::bind(&server);
    let client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Mover", 100.0, 100.0);
    c.unit.hp = 500;
    c.unit.max_hp = 1000;
    let player = spawn_player(&server, &client, c, PLAYER_OBJ);
    let obj = WorldObject::from(&player);
    super::buffs::add_buff_by_id(&server, &obj, BUFF_ID + 2, &obj, 0);
    assert!(player.data().unit.buffs.check_buff(BUFF_ID + 2));
    // no velocity and no position change: the buff stays
    let pos = {
        let t = player.data().unit.transform;
        (t.x, t.y, t.z)
    };
    super::on_move_request(&server, &player, 0, 0, 0);
    super::on_moved(&server, &player, pos, 0);
    assert!(player.data().unit.buffs.check_buff(BUFF_ID + 2));
    // a moving velocity removes it (CSMoveUnitPacket.RemoveEffects)
    super::on_move_request(&server, &player, 10, 0, 0);
    assert!(!player.data().unit.buffs.check_buff(BUFF_ID + 2));
    // and a position change removes a re-applied one (Character.SetPosition)
    super::buffs::add_buff_by_id(&server, &obj, BUFF_ID + 2, &obj, 0);
    super::on_moved(&server, &player, (pos.0 - 1.0, pos.1, pos.2), 0);
    assert!(!player.data().unit.buffs.check_buff(BUFF_ID + 2));
}
