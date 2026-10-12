//! Removal-reason regressions: a catch dispels FISH before its lethal damage; only an
//! actual timer expiry may invoke the FISH escape Timeout trigger.

use std::sync::Arc;
use std::time::Duration;

use crate::data::GameData;
use crate::models::npc::Npc;
use crate::server::GameServer;
use crate::systems::skills::data::effects::{BuffEffect, DispelEffect, RestoreManaEffect};
use crate::systems::skills::data::{
    BuffTemplate, BuffTriggerTemplate, EffectKind, EffectTemplate, TickEffect,
};
use crate::systems::skills::effects::{combat, EffectArgs, EffectSource};
use crate::systems::skills::geometry::Target;
use crate::systems::skills::types::{
    BuffEventTriggerKind, CastAction, SkillCastTarget, SkillCaster, SkillObject,
};
use crate::test_support::test_server_with;
use crate::world::{Entity, NpcRef, WorldObject};

const FISH: u32 = 5357;
const FISH_TAG: u32 = 1025;
const TIMEOUT_MARKER: u32 = 61_001;
const DISPEL_MARKER: u32 = 61_002;

fn fixture(duration: i32) -> (Arc<GameServer>, NpcRef, WorldObject) {
    refresh_fixture(FISH, duration, 0.0)
}

fn refresh_fixture(
    buff_id: u32,
    duration: i32,
    tick: f64,
) -> (Arc<GameServer>, NpcRef, WorldObject) {
    let mut data = GameData::empty();
    let skills = &mut data.systems.skills;
    for id in [buff_id, TIMEOUT_MARKER, DISPEL_MARKER] {
        skills.add_buff(BuffTemplate {
            id,
            kind: 1,
            duration: if id == buff_id { duration } else { 0 },
            stack_rule: if id == buff_id {
                crate::data::skills::stack_rule::REFRESH
            } else {
                0
            },
            tick: if id == buff_id { tick } else { 0.0 },
            tick_effects: if id == buff_id && tick > 0.0 {
                vec![TickEffect {
                    effect_id: 61_003,
                    target_buff_tag_id: 0,
                    target_no_buff_tag_id: 0,
                }]
            } else {
                Vec::new()
            },
            ..BuffTemplate::default()
        });
    }
    skills.add_effect(
        EffectKind::RestoreManaEffect,
        EffectTemplate::RestoreMana(RestoreManaEffect {
            id: 61_003,
            use_fixed_value: true,
            fixed_min: 1,
            fixed_max: 1,
            use_level_value: false,
            level_md: 0.0,
            level_va_start: 0,
            level_va_end: 0,
            percent: false,
        }),
        61_003,
    );
    skills.buff_tags.insert(FISH, vec![FISH_TAG]);
    skills.tagged_buffs.insert(FISH_TAG, vec![FISH]);
    let mut triggers = Vec::new();
    // Observable marker effects distinguish event emission without relying on escape
    // spawn-manager side effects. The ignored gate below pins the real escape chain.
    for (id, kind, marker) in [
        (1, BuffEventTriggerKind::Timeout, TIMEOUT_MARKER),
        (2, BuffEventTriggerKind::Dispelled, DISPEL_MARKER),
    ] {
        skills.add_effect(
            EffectKind::BuffEffect,
            EffectTemplate::Buff(BuffEffect {
                id,
                buff_id: Some(marker),
                chance: 100,
                stack: 1,
                ab_level: 0,
            }),
            0,
        );
        triggers.push(BuffTriggerTemplate {
            id,
            kind: kind as u16,
            effect_on_source: false,
            effect: (EffectKind::BuffEffect, id),
            use_damage_amount: false,
            use_original_source: false,
            target_buff_tag_id: 0,
            target_no_buff_tag_id: 0,
            synergy: false,
        });
    }
    skills.buff_triggers.insert(buff_id, triggers);
    let server = Arc::new(test_server_with(data));
    server.set_self();
    crate::systems::skills::bind(&server);
    let mut npc = Npc::new(13072);
    npc.unit.hp = 1480;
    npc.unit.max_hp = 18689;
    npc.unit.mp = 0;
    npc.unit.max_mp = 1000;
    let npc = Entity::new(0x300, npc);
    let obj = WorldObject::from(&npc);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&obj, &server.view_ctx());
    super::add_buff_by_id(&server, &obj, buff_id, &obj, 0);
    assert!(npc.data().unit.buffs.check_buff(buff_id));
    (server, npc, obj)
}

fn assert_explicit_removal(npc: &NpcRef) {
    let npc = npc.data();
    assert!(!npc.unit.buffs.check_buff(FISH));
    assert!(!npc.unit.buffs.check_buff(TIMEOUT_MARKER));
    assert!(npc.unit.buffs.check_buff(DISPEL_MARKER));
    assert_eq!(npc.unit.hp, 1480);
}

#[tokio::test]
async fn fish_catch_dispel_does_not_fire_escape_timeout_or_late_timer() {
    let (server, npc, obj) = fixture(10_000);
    let buff = npc.data().unit.buffs.effects[0].clone();
    let skill_object = SkillObject::None;
    combat::dispel(
        &server,
        &DispelEffect {
            id: 791,
            dispel_count: 1,
            cure_count: 1,
            buff_tag_id: FISH_TAG,
        },
        &EffectArgs {
            caster: Some(obj.clone()),
            caster_caster: SkillCaster::Unit(obj.obj_id()),
            target: Target::Obj(obj.clone()),
            target_caster: SkillCastTarget::Unit(obj.obj_id()),
            cast: CastAction::Skill {
                skill_id: 21322,
                tl_id: 1,
            },
            source: EffectSource::default(),
            skill_object: &skill_object,
        },
    );
    assert_explicit_removal(&npc);
    // An already queued task for this buff must not turn a completed dispel into expiry.
    super::dispel_task(&server, obj.obj_id(), buff.index, buff.task_gen);
    assert_explicit_removal(&npc);
}

#[tokio::test]
async fn explicit_buff_removal_and_tag_removal_keep_dispelled_without_timeout() {
    for tagged in [false, true] {
        let (server, npc, obj) = fixture(10_000);
        if tagged {
            super::remove_buffs_by_tag(&server, &obj, FISH_TAG, 1);
        } else {
            // Exercises on_removed's bookkeeping path, independently of explicit exit.
            super::remove_buff(&server, &obj, FISH);
        }
        assert_explicit_removal(&npc);
    }
}

#[tokio::test]
async fn natural_fish_buff_expiry_still_fires_timeout() {
    let (_server, npc, _obj) = fixture(30);
    for _ in 0..100 {
        if !npc.data().unit.buffs.check_buff(FISH) {
            break;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    let npc = npc.data();
    assert!(!npc.unit.buffs.check_buff(FISH));
    assert!(npc.unit.buffs.check_buff(TIMEOUT_MARKER));
    assert!(npc.unit.buffs.check_buff(DISPEL_MARKER));
}

#[tokio::test]
async fn permanent_rod_refresh_stays_active_without_timeout_or_dispelled_events() {
    // Actual1.2 rod27312 uses5720: duration0, tick0, stack rule Refresh.
    const ROD: u32 = 5720;
    let (server, npc, obj) = refresh_fixture(ROD, 0, 0.0);
    let before = npc.data().unit.buffs.effects[0].clone();
    super::add_buff_by_id(&server, &obj, ROD, &obj, 0);
    let refreshed = super::get_buff(&obj, before.index).unwrap();
    assert_eq!(refreshed.duration, 0);
    assert_ne!(refreshed.task_gen, before.task_gen);
    // An obsolete task cannot expire the refreshed permanent effect either.
    super::dispel_task(&server, obj.obj_id(), before.index, before.task_gen);
    tokio::time::sleep(Duration::from_millis(20)).await;
    let npc = npc.data();
    assert!(npc.unit.buffs.check_buff(ROD));
    assert_eq!(npc.unit.buffs.buff_count_by_id(ROD), 1);
    assert!(!npc.unit.buffs.check_buff(TIMEOUT_MARKER));
    assert!(!npc.unit.buffs.check_buff(DISPEL_MARKER));
}

#[tokio::test]
async fn refreshed_finite_buff_keeps_timeout_and_invalidates_previous_timer() {
    let (server, npc, obj) = fixture(30);
    let before = npc.data().unit.buffs.effects[0].clone();
    super::add_buff_by_id(&server, &obj, FISH, &obj, 0);
    let refreshed = super::get_buff(&obj, before.index).unwrap();
    assert_ne!(refreshed.task_gen, before.task_gen);
    super::dispel_task(&server, obj.obj_id(), before.index, before.task_gen);
    assert!(npc.data().unit.buffs.check_buff(FISH));
    for _ in 0..100 {
        if !npc.data().unit.buffs.check_buff(FISH) {
            break;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    let npc = npc.data();
    assert!(!npc.unit.buffs.check_buff(FISH));
    assert!(npc.unit.buffs.check_buff(TIMEOUT_MARKER));
    assert!(npc.unit.buffs.check_buff(DISPEL_MARKER));
}

#[tokio::test]
async fn refreshed_permanent_ticking_buff_keeps_repeated_tick_effects() {
    const TICKING: u32 = 61_004;
    let (server, npc, obj) = refresh_fixture(TICKING, 0, 20.0);
    for _ in 0..100 {
        if npc.data().unit.mp >= 2 {
            break;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    assert!(npc.data().unit.mp >= 2);
    let before = npc.data().unit.buffs.effects[0].clone();
    super::add_buff_by_id(&server, &obj, TICKING, &obj, 0);
    let refreshed = super::get_buff(&obj, before.index).unwrap();
    assert_eq!(refreshed.duration, 0);
    assert_eq!(refreshed.count, -1);
    assert_ne!(refreshed.task_gen, before.task_gen);
    let mana = npc.data().unit.mp;
    super::dispel_task(&server, obj.obj_id(), before.index, before.task_gen);
    assert_eq!(npc.data().unit.mp, mana);
    for _ in 0..100 {
        if npc.data().unit.mp >= mana + 2 {
            break;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    let npc = npc.data();
    assert!(npc.unit.mp >= mana + 2);
    assert!(npc.unit.buffs.check_buff(TICKING));
    assert!(!npc.unit.buffs.check_buff(TIMEOUT_MARKER));
    assert!(!npc.unit.buffs.check_buff(DISPEL_MARKER));
}

#[tokio::test]
async fn queued_created_buff_removal_finishes_without_escape_or_subscription_leak() {
    let (server, npc, obj) = fixture(10_000);
    super::remove_buff(&server, &obj, FISH);
    super::remove_buff(&server, &obj, DISPEL_MARKER);
    let template =
        super::net_template(&server, server.game_data.systems.skills.buff(FISH).unwrap());
    let mut buff = crate::models::buff::Buff::new(
        template,
        super::unit_caster(obj.obj_id()),
        Some((13072, 1)),
        crate::world::now_ms(),
    );
    buff.duration = 10_000;
    // An item/skill operation may request removal before its queued Started event runs.
    npc.data()
        .unit
        .buffs
        .add_buff_with_duration(buff, 99, crate::world::now_ms());
    super::exit(&server, &obj, 99, false);
    assert!(!npc.data().unit.buffs.check_buff(DISPEL_MARKER));
    crate::systems::dispatch_buff_events(&server, &obj);
    assert_explicit_removal(&npc);
    assert!(!server
        .systems
        .skills
        .triggers
        .lock()
        .unwrap()
        .iter()
        .any(|s| { s.owner_obj == obj.obj_id() && s.buff_index == 99 }));
    super::dispel_task(&server, obj.obj_id(), 99, 0);
    assert_explicit_removal(&npc);
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_fish_catch_dispel_and_escape_timeout_chain() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let db =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let timeout_escape: (u16, u32) = db.query_row(
        "SELECT t.event_id,s.value1 FROM buff_triggers t JOIN effects e ON e.id=t.effect_id JOIN special_effects s ON e.actual_type='SpecialEffect' AND s.id=e.actual_id WHERE t.buff_id=5357 AND s.special_effect_type_id=33",
        [], |r| Ok((r.get(0)?, r.get(1)?)),
    ).unwrap();
    assert_eq!(
        timeout_escape,
        (BuffEventTriggerKind::Timeout as u16, 21616)
    );
    let dispel_tag: u32 = db.query_row(
        "SELECT d.buff_tag_id FROM skills s JOIN plot_events p ON p.plot_id=s.plot_id JOIN plot_effects e ON e.event_id=p.id JOIN dispel_effects d ON d.id=e.actual_id AND e.actual_type='DispelEffect' WHERE s.id=21322 AND p.id=6327",
        [], |r| r.get(0),
    ).unwrap();
    assert_eq!(dispel_tag, FISH_TAG);
    let tagged: u32 = db
        .query_row(
            "SELECT buff_id FROM tagged_buffs WHERE buff_id=5357 AND tag_id=1025",
            [],
            |r| r.get(0),
        )
        .unwrap();
    assert_eq!(tagged, FISH);
    let escape: (u32, String) = db.query_row(
        "SELECT k.npc_id,k.vanish FROM skill_effects s JOIN effects e ON e.id=s.effect_id JOIN kill_npc_without_corpse_effects k ON e.actual_type='KillNpcWithoutCorpseEffect' AND k.id=e.actual_id WHERE s.skill_id=21616 AND k.npc_id=13072",
        [], |r| Ok((r.get(0)?, r.get(1)?)),
    ).unwrap();
    assert_eq!(escape, (13072, "t".to_owned()));
}
