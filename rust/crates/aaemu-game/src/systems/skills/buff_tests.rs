//! Port of AAEmu.UnitTests/Game/Models/Game/Skills/BuffTests.cs
//!
//! `Buff` is split between the bookkeeping data (`models::buff::Buff`, constructed with
//! `Buff::new(template, skillCaster, caster, time)`) and the behaviour on its owner
//! (`systems::skills::buffs`: `consume_charge`, `start`, `buff_created_packet`), which works on
//! a buff held by a unit. The owner / caster references are ids, the skill is `skill_id`
//! (`Skill?.Template.Id`, 0 for `new Skill()`), the clock is an argument (unix ms).
//! `EndTime`, `Events`, `Triggers` and `saveFactions` have no Rust field (see the mapping in the
//! port report).

use std::sync::Arc;

use aaemu_commons::{Marshal, PacketStream};

use crate::data::skills::{BuffKind, BuffTemplate as NetBuffTemplate};
use crate::data::GameData;
use crate::models::buff::{effect_state, Buff, SkillCaster};
use crate::models::npc::Npc;
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};
use crate::server::GameServer;
use crate::test_support::test_server_with;
use crate::world::{now_ms, Entity, NpcRef, WorldObject};

use super::buffs;
use super::data::BuffTemplate;

const NOW: i64 = 1_700_000_000_000;
const BONUS_BUFF: u32 = 60_001;

/// `CreateBuff()`: default template, `SkillCasterUnit(1)`, no caster unit, `new Skill()`.
fn create_buff(start_time: i64) -> Buff {
    Buff::new(
        NetBuffTemplate::simple(0, BuffKind::Good),
        SkillCaster { kind: 0, obj_id: 1 },
        None,
        start_time,
    )
}

// ---- Constructor ----

#[test]
fn constructor_with_parameters_sets_properties_correctly() {
    let skill_caster = SkillCaster { kind: 0, obj_id: 1 };
    let template = NetBuffTemplate::simple(0, BuffKind::Good);
    let buff = Buff::new(template.clone(), skill_caster, Some((7, 3)), NOW);
    assert_eq!(buff.caster, Some((7, 3)));
    assert_eq!(buff.skill_caster, skill_caster);
    assert_eq!(buff.template, template);
    assert_eq!(buff.skill_id, 0); // new Skill()
    assert_eq!(buff.start_time_ms, NOW);
    assert_eq!(buff.ab_level, 1);
    assert_eq!(buff.state, effect_state::CREATED);
    assert!(!buff.in_use);
    assert_eq!(buff.duration, 0);
    assert_eq!(buff.tick, 0.0);
    assert_eq!(buff.charge, 0);
    assert!(!buff.passive);
}

#[test]
fn constructor_with_null_parameters_does_not_throw() {
    let buff = Buff::new(
        NetBuffTemplate::simple(0, BuffKind::Good),
        SkillCaster::default(),
        None,
        now_ms(),
    );
    assert_eq!(buff.caster, None);
}

// ---- Properties ----

#[test]
fn index_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.index = 5;
    assert_eq!(buff.index, 5);
}

#[test]
fn state_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.state = effect_state::ACTING;
    assert_eq!(buff.state, effect_state::ACTING);
}

#[test]
fn in_use_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.in_use = true;
    assert!(buff.in_use);
}

#[test]
fn duration_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.duration = 10000;
    assert_eq!(buff.duration, 10000);
}

#[test]
fn tick_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.tick = 500.5;
    assert_eq!(buff.tick, 500.5);
}

#[test]
fn charge_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.charge = 3;
    assert_eq!(buff.charge, 3);
}

#[test]
fn passive_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.passive = true;
    assert!(buff.passive);
}

#[test]
fn ab_level_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.ab_level = 10;
    assert_eq!(buff.ab_level, 10);
}

// ---- IsEnded ----

#[test]
fn is_ended_returns_correct_value() {
    for (state, expected) in [
        (effect_state::FINISHED, true),
        (effect_state::FINISHING, true),
        (effect_state::CREATED, false),
        (effect_state::ACTING, false),
    ] {
        let mut buff = create_buff(NOW);
        buff.state = state;
        assert_eq!(buff.is_ended(), expected, "state {state}");
    }
}

// ---- GetTimeLeft ----

#[test]
fn get_time_left_with_zero_duration_returns_negative_one() {
    let mut buff = create_buff(NOW);
    buff.duration = 0;
    assert_eq!(buff.time_left(NOW), -1.0);
}

#[test]
fn get_time_left_with_positive_duration_returns_time_left() {
    let mut buff = create_buff(NOW - 5000); // started 5 s ago
    buff.duration = 10000;
    let result = buff.time_left(NOW);
    assert!(result >= 0.0);
    assert!(result <= 5000.0);
}

#[test]
fn get_time_left_with_expired_duration_returns_zero() {
    let mut buff = create_buff(NOW - 10_000); // 10 s ago, 1 s duration
    buff.duration = 1000;
    assert_eq!(buff.time_left(NOW), 0.0);
}

// ---- GetTimeElapsed ----

#[test]
fn get_time_elapsed_returns_positive_value() {
    let buff = create_buff(now_ms() - 3000);
    assert!(buff.time_elapsed(now_ms()) >= 3000);
}

#[test]
fn get_time_elapsed_at_start_time_returns_zero() {
    let now = now_ms();
    let buff = create_buff(now);
    assert_eq!(buff.time_elapsed(now), 0);
}

// ---- server-backed fixture (behaviour on a unit) ----

fn bonus_data() -> GameData {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        id: BONUS_BUFF,
        kind: 1,
        bonuses: vec![BonusTemplate {
            attribute: UnitAttribute::Str,
            modifier_type: UnitModifierType::Value,
            value: 100,
            linear_level_bonus: 0,
        }],
        ..BuffTemplate::default()
    });
    data
}

fn owner() -> (NpcRef, WorldObject) {
    let npc = Entity::new(0x300, Npc::new(1));
    let obj = WorldObject::from(&npc);
    (npc, obj)
}

/// Puts `buff` on the owner at `index` (`Buffs.AddBuff` bookkeeping; events not dispatched).
fn put(npc: &NpcRef, buff: Buff, index: u32) {
    npc.data()
        .unit
        .buffs
        .add_buff_with_duration(buff, index, now_ms());
}

// ---- WriteData ----

/// C#: `WriteData` writes `Pisc(Charge, Duration / 10, 0, Template.Tick / 10)` (the test only
/// checks the stream exists). Rust: the same tail of `SCBuffCreatedPacket`, built from a buff on
/// a unit by `buff_created_packet`.
#[tokio::test]
async fn write_data_writes_correct_data() {
    let server = Arc::new(test_server_with(GameData::empty()));
    let (npc, obj) = owner();
    let mut buff = create_buff(now_ms());
    buff.charge = 5;
    buff.duration = 10000;
    buff.tick = 1000.0;
    put(&npc, buff, 10);
    // `add_buff_with_duration` copies `Template.Tick` into `Tick`, as `Buffs.AddBuff` does.
    let b = npc.data().unit.buffs.effect_by_index(10).cloned().unwrap();
    let p = buffs::buff_created_packet(&server, &obj, &b);
    let mut body = PacketStream::new();
    p.write(&mut body);
    let mut tail = PacketStream::new();
    tail.write_pisc(&[5, 1000, 0, 0]); // Template.Tick = 0
    assert!(body.into_bytes().ends_with(&tail.into_bytes()));
}

// ---- ConsumeCharge ----

#[tokio::test]
async fn consume_charge_returns_correct_value() {
    let server = Arc::new(test_server_with(GameData::empty()));
    for (initial, consume, expected_charge, expected_returned) in
        [(10, 5, 5, 0), (3, 5, 0, 2), (0, 5, 0, 5), (5, 5, 0, 0)]
    {
        let (npc, obj) = owner();
        let mut buff = create_buff(now_ms());
        buff.charge = initial;
        put(&npc, buff, 10);
        let result = buffs::consume_charge(&server, &obj, 10, consume);
        let charge = npc.data().unit.buffs.effect_by_index(10).unwrap().charge;
        assert_eq!(charge, expected_charge, "charge {initial} - {consume}");
        assert_eq!(result, expected_returned, "charge {initial} - {consume}");
    }
}

// ---- Edge cases ----

#[test]
fn skill_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    buff.skill_id = 42;
    assert_eq!(buff.skill_id, 42);
}

#[test]
fn template_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    let template = NetBuffTemplate::simple(77, BuffKind::Bad);
    buff.template = template.clone();
    assert_eq!(buff.template, template);
}

#[test]
fn skill_caster_set_and_get_returns_correct_value() {
    let mut buff = create_buff(NOW);
    let skill_caster = SkillCaster {
        kind: 0,
        obj_id: 42,
    };
    buff.skill_caster = skill_caster;
    assert_eq!(buff.skill_caster, skill_caster);
}

// ---- Bonus application ----

/// `CreateBonusBuff`: passive (no `SCBuffCreatedPacket` broadcast in `Start`), ab level 1.
fn bonus_buff(server: &GameServer, npc: &NpcRef, index: u32) {
    let template = buffs::net_template(
        server,
        server.game_data.systems.skills.buff(BONUS_BUFF).unwrap(),
    );
    let mut buff = Buff::new(template, SkillCaster { kind: 0, obj_id: 1 }, None, now_ms());
    buff.passive = true;
    buff.ab_level = 1;
    put(npc, buff, index);
}

fn str_bonuses(npc: &NpcRef) -> Vec<i32> {
    npc.data()
        .unit
        .bonuses
        .bonuses(UnitAttribute::Str)
        .iter()
        .map(|b| b.value)
        .collect()
}

#[tokio::test]
async fn start_applies_bonus_once() {
    let server = Arc::new(test_server_with(bonus_data()));
    let (npc, obj) = owner();
    bonus_buff(&server, &npc, 10);
    buffs::start(&server, &obj, 10);
    assert_eq!(str_bonuses(&npc), vec![100]);
}

/// A refresh runs `Start` a second time for the same index: the bonus must not double.
#[tokio::test]
async fn start_called_twice_does_not_duplicate_bonuses() {
    let server = Arc::new(test_server_with(bonus_data()));
    let (npc, obj) = owner();
    bonus_buff(&server, &npc, 10);
    buffs::start(&server, &obj, 10);
    buffs::start(&server, &obj, 10);
    assert_eq!(str_bonuses(&npc), vec![100]);
}
