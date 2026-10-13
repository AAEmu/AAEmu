use std::sync::Arc;
use std::time::Duration;

use crate::data::GameData;
use crate::models::npc::Npc;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::object::Entity;
use crate::world::{NpcRef, PlayerRef, WorldObject};

use super::data::{BuffTemplate, EffectKind, EffectTemplate, SkillTemplate, UnitReq};
use super::geometry::Target;
use super::skill::{use_skill, CastCtx};
use super::state::{SkillRef, SkillRun};
use super::types::{skill_result, SkillCastTarget, SkillCaster, SkillHitType, SkillObject};

pub(super) fn fixture(data: GameData) -> (Arc<crate::GameServer>, TestClient, PlayerRef, NpcRef) {
    let server = Arc::new(test_server_with(data));
    super::bind(&server);
    let client = TestClient::new(&server, 1, 1);
    let mut character = test_character(1, "Caster", 100.0, 100.0);
    character.unit.hp = 1000;
    character.unit.mp = 100;
    character.unit.max_mp = 1000;
    let player = spawn_player(&server, &client, character, 0x100);
    let mut n = Npc::new(1);
    n.unit.hp = 1000;
    n.unit.max_hp = 1000;
    n.unit.transform.set_xyz(100.0, 105.0, 100.0);
    let npc = Entity::new(0x200, n);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&npc, &server.view_ctx());
    (server, client, player, npc)
}

fn cast(
    server: &crate::GameServer,
    skill: &SkillRef,
    player: &PlayerRef,
    npc: &NpcRef,
) -> (u8, u32) {
    use_skill(
        server,
        skill,
        &WorldObject::from(player),
        SkillCaster::Unit(player.obj_id),
        &mut SkillCastTarget::Unit(npc.obj_id),
        Some(SkillObject::None),
        false,
    )
}

#[tokio::test]
async fn or_requirements_require_one_success_and_evaluate_later_alternatives() {
    let template = SkillTemplate {
        id: 1,
        or_unit_reqs: true,
        ..Default::default()
    };
    for (levels, passes) in [
        (vec![20, 30], false),
        (vec![20, 5], true),
        (vec![5, 20], true),
        (vec![], true),
    ] {
        let mut data = GameData::empty();
        data.systems.skills.unit_reqs.insert(
            "Skill".into(),
            levels
                .into_iter()
                .enumerate()
                .map(|(i, level)| UnitReq {
                    id: i as u32 + 1,
                    owner_id: 1,
                    owner_type: "Skill".into(),
                    kind: 1,
                    value1: level,
                    value2: 0,
                })
                .collect(),
        );
        let (server, _client, player, _npc) = fixture(data);
        assert_eq!(
            super::unit_reqs::can_use_skill(
                &server,
                &template,
                &WorldObject::from(player.clone()),
                &SkillCaster::Unit(player.obj_id)
            )
            .is_ok(),
            passes
        );
    }
}

#[tokio::test]
async fn rejected_source_and_target_states_do_not_mutate_cast_resources() {
    for (dead_source, dead_target, swimming, only_water, code) in [
        (true, false, false, false, "skill_source_died"),
        (false, true, false, false, "skill_target_died"),
        (false, false, true, false, "skill_cannot_cast_in_swimming"),
        (false, false, false, true, "skill_invalid_location"),
    ] {
        let (server, _client, player, npc) = fixture(GameData::empty());
        let template = SkillTemplate {
            source_alive: true,
            target_alive: true,
            source_not_swim: swimming,
            target_only_water: only_water,
            mana_cost: 25,
            cancel_ongoing_buffs: true,
            ..super::tests::skill_template(1)
        };
        if dead_source {
            player.data().unit.hp = 0;
        }
        if dead_target {
            npc.data().unit.hp = 0;
        }
        player.data().unit.is_under_water = swimming;
        let skill = SkillRun::new(Arc::new(template), 1);
        assert_eq!(
            cast(&server, &skill, &player, &npc),
            (super::unit_reqs::skill_result_for_key(code), 0)
        );
        let p = player.data();
        assert_eq!(p.unit.mp, 100);
        assert_eq!(p.unit.global_cooldown_ms, 0);
        assert_eq!(p.unit.skill_last_used_ms, 0);
        assert!(p.unit.skill_state.skill_task.is_none());
        assert_eq!(skill.tl_id(), 0);
    }
}

#[tokio::test]
async fn out_of_range_plot_is_rejected_before_starting_a_plot_or_gcd() {
    let (server, _client, player, npc) = fixture(GameData::empty());
    npc.data().unit.transform.set_xyz(100.0, 200.0, 100.0);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            plot_id: Some(77),
            plot_only: true,
            ..super::tests::skill_template(1)
        }),
        1,
    );
    assert_eq!(
        cast(&server, &skill, &player, &npc),
        (skill_result::TOO_FAR_RANGE, 0)
    );
    assert!(player.data().unit.skill_state.active_plot.is_none());
    assert_eq!(player.data().unit.skill_last_used_ms, 0);
    assert_eq!(skill.tl_id(), 0);
}

#[tokio::test]
async fn guaranteed_hit_is_per_effect_and_does_not_change_the_cast_miss_cache() {
    let mut data = GameData::empty();
    data.systems.skills.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
        1,
    );
    let mut template = super::tests::skill_template(1);
    template.level_rule_no_consideration = false;
    let mut effect = super::tests::skill_effect(1, EffectKind::DamageEffect, 1);
    effect.always_hit = true;
    template.effects.push(effect);
    let (server, _client, player, npc) = fixture(data);
    let skill = SkillRun::new(Arc::new(template), 1);
    skill
        .state()
        .hit_types
        .insert(npc.obj_id, SkillHitType::MeleeMiss);
    let ctx = CastCtx {
        caster: WorldObject::from(player.clone()),
        caster_caster: SkillCaster::Unit(player.obj_id),
        target: Target::Obj(WorldObject::from(npc.clone())),
        target_caster: SkillCastTarget::Unit(npc.obj_id),
        skill_object: SkillObject::None,
    };
    super::skill::apply_effects(&server, &skill, &ctx);
    assert_eq!(npc.data().unit.hp, 900);
    assert_eq!(skill.hit_type(npc.obj_id), Some(SkillHitType::MeleeMiss));
}

#[tokio::test]
async fn channel_buffs_have_distinct_owners_and_cancel_stops_mana_ticks() {
    let mut data = GameData::empty();
    for id in [1, 2] {
        data.systems.skills.add_buff(BuffTemplate {
            id,
            kind: 1,
            duration: 10_000,
            bonuses: vec![crate::models::unit::BonusTemplate {
                attribute: crate::models::unit::UnitAttribute::MaxHealth,
                modifier_type: crate::models::unit::UnitModifierType::Value,
                value: 1000,
                linear_level_bonus: 0,
            }],
            ..Default::default()
        });
    }
    let (server, _client, player, npc) = fixture(data);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            channeling_time: 1000,
            channeling_tick: 20,
            channeling_mana: 7,
            channeling_buff_id: 1,
            channeling_target_buff_id: 2,
            ..super::tests::skill_template(1)
        }),
        1,
    );
    assert_eq!(
        cast(&server, &skill, &player, &npc).0,
        skill_result::SUCCESS
    );
    assert!(player.data().unit.buffs.check_buff(1));
    assert!(!player.data().unit.buffs.check_buff(2));
    assert!(npc.data().unit.buffs.check_buff(2));
    assert!(!npc.data().unit.buffs.check_buff(1));
    let deadline = tokio::time::Instant::now() + Duration::from_millis(500);
    while player.data().unit.mp > 86 && tokio::time::Instant::now() < deadline {
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    assert!(
        player.data().unit.mp <= 86,
        "channel did not spend mana; hp={}",
        player.data().unit.hp
    );
    super::skill::stop(&server, &skill, &WorldObject::from(player.clone()), 0);
    let mp = player.data().unit.mp;
    assert!(!player.data().unit.buffs.check_buff(1));
    assert!(!npc.data().unit.buffs.check_buff(2));
    tokio::time::sleep(Duration::from_millis(60)).await;
    assert_eq!(player.data().unit.mp, mp);
    assert!(player.data().unit.skill_state.skill_task.is_none());
}

#[test]
fn percentage_endpoints_and_cone_direction_are_exact() {
    assert!(!super::effects::chance_succeeds(0));
    assert!(!super::effects::chance_succeeds(-1));
    assert!(super::effects::chance_succeeds(100));
    let origin = crate::models::transform::PositionAndRotation::default();
    let forward = crate::models::transform::PositionAndRotation {
        y: 5.0,
        ..Default::default()
    };
    let behind = crate::models::transform::PositionAndRotation {
        y: -5.0,
        ..Default::default()
    };
    let sideways = crate::models::transform::PositionAndRotation {
        x: 5.0,
        ..Default::default()
    };
    assert!(super::geometry::in_angle(&origin, &forward, 90.0));
    assert!(!super::geometry::in_angle(&origin, &behind, 90.0));
    assert!(!super::geometry::in_angle(&origin, &sideways, 90.0));
    assert!(super::geometry::in_angle(&origin, &behind, 360.0));
    let turned = crate::models::transform::PositionAndRotation {
        rot_z: std::f32::consts::FRAC_PI_2,
        ..origin
    };
    let west = crate::models::transform::PositionAndRotation {
        x: -5.0,
        ..Default::default()
    };
    assert!(super::geometry::in_angle(&turned, &west, 90.0));
    assert!(!super::geometry::in_angle(&turned, &forward, 90.0));
}

#[tokio::test]
async fn magic_misses_do_no_damage_but_guaranteed_hit_bypasses_them() {
    for guaranteed in [false, true] {
        let (server, _client, player, npc) = fixture(GameData::empty());
        let skill = SkillRun::new(Arc::new(super::tests::skill_template(1)), 1);
        skill
            .state()
            .hit_types
            .insert(npc.obj_id, SkillHitType::SpellMiss);
        let mut effect = super::tests::damage_effect(1, 100);
        effect.damage_type = 2;
        let mut source = super::effects::EffectSource::from_skill(skill.clone());
        source.always_hit = guaranteed;
        super::effects::combat::damage(
            &server,
            &effect,
            &super::effects::EffectArgs {
                caster: Some(WorldObject::from(&player)),
                caster_caster: SkillCaster::Unit(player.obj_id),
                target: Target::Obj(WorldObject::from(&npc)),
                target_caster: SkillCastTarget::Unit(npc.obj_id),
                cast: super::types::CastAction::Skill {
                    skill_id: 1,
                    tl_id: 1,
                },
                source,
                skill_object: &SkillObject::None,
            },
            None,
        );
        assert_eq!(npc.data().unit.hp, if guaranteed { 900 } else { 1000 });
        assert_eq!(skill.hit_type(npc.obj_id), Some(SkillHitType::SpellMiss));
    }
}

#[tokio::test]
async fn stun_and_walking_restrictions_allow_only_configured_casts() {
    for (stunned, walking) in [(true, false), (false, true)] {
        let mut data = GameData::empty();
        data.systems.skills.add_buff(BuffTemplate {
            id: 10,
            stun: stunned,
            ..Default::default()
        });
        let (server, _client, player, npc) = fixture(data);
        let caster = WorldObject::from(&player);
        // Keep the live unit stats; the validation reads the actual active buff set.
        let mut b = crate::models::buff::Buff::new(
            super::buffs::net_template(&server, server.game_data.systems.skills.buff(10).unwrap()),
            super::buffs::unit_caster(player.obj_id),
            Some((1, 10)),
            crate::world::now_ms(),
        );
        b.state = crate::models::buff::effect_state::ACTING;
        player.data().unit.buffs.effects.push(b);
        player.data().unit.skill_state.moving = walking;
        let skill = SkillRun::new(
            Arc::new(SkillTemplate {
                source_cannot_use_while_walk: walking,
                ..super::tests::skill_template(1)
            }),
            1,
        );
        let expected = if stunned {
            "skill_cannot_cast_in_stun"
        } else {
            "skill_cannot_cast_while_walking"
        };
        assert_eq!(
            cast(&server, &skill, &player, &npc).0,
            super::unit_reqs::skill_result_for_key(expected)
        );
        assert_eq!(player.data().unit.skill_last_used_ms, 0);
        if stunned {
            let skill = SkillRun::new(
                Arc::new(SkillTemplate {
                    source_stun: true,
                    ..super::tests::skill_template(2)
                }),
                1,
            );
            assert_eq!(
                super::validation::check(
                    &server,
                    &skill,
                    &caster,
                    &Target::Obj(WorldObject::from(&npc))
                ),
                Ok(())
            );
        }
    }
}

#[tokio::test]
async fn channel_without_ticks_applies_once_and_cancellation_has_one_end_notification() {
    let mut data = GameData::empty();
    data.systems.skills.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
        1,
    );
    let (server, mut client, player, npc) = fixture(data);
    let template = SkillTemplate {
        channeling_time: 30,
        effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
        ..super::tests::skill_template(100)
    };
    let skill = SkillRun::new(Arc::new(template.clone()), 1);
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
    assert_eq!(npc.data().unit.hp, 1000);
    let deadline = tokio::time::Instant::now() + Duration::from_secs(1);
    while skill.tl_id() != 0 && tokio::time::Instant::now() < deadline {
        tokio::time::sleep(Duration::from_millis(5)).await;
    }
    assert_eq!(npc.data().unit.hp, 900);
    assert_eq!(skill.tl_id(), 0);
    assert_eq!(
        client
            .drain()
            .iter()
            .filter(|(op, _)| *op == crate::opcodes::sc::SCSkillEndedPacket)
            .count(),
        1
    );
    // Start directly with a new timeline to isolate cancellation from GCD timing.
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            channeling_time: 200,
            ..template
        }),
        1,
    );
    skill.state().tl_id = server.systems.skills.tl_ids.next_id();
    skill.state().initial_target = npc.obj_id;
    let ctx = CastCtx {
        caster: WorldObject::from(&player),
        caster_caster: SkillCaster::Unit(player.obj_id),
        target: Target::Obj(WorldObject::from(&npc)),
        target_caster: SkillCastTarget::Unit(npc.obj_id),
        skill_object: SkillObject::None,
    };
    super::skill::start_channeling(&server, &skill, &ctx);
    client.drain();
    super::skill::stop(&server, &skill, &ctx.caster, 0);
    super::skill::stop(&server, &skill, &ctx.caster, 0);
    assert_eq!(
        client
            .drain()
            .iter()
            .filter(|(op, _)| *op == crate::opcodes::sc::SCSkillEndedPacket)
            .count(),
        1
    );
    tokio::time::sleep(Duration::from_millis(230)).await;
    assert_eq!(npc.data().unit.hp, 900);
    assert!(client
        .drain()
        .iter()
        .all(|(op, _)| *op != crate::opcodes::sc::SCSkillEndedPacket));
}

#[tokio::test]
async fn a_canceled_skill_can_be_reused_and_old_tasks_cannot_finish_it() {
    let mut data = GameData::empty();
    data.systems.skills.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
        1,
    );
    let (server, mut client, player, npc) = fixture(data);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            casting_time: 30,
            effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
            ..super::tests::skill_template(100)
        }),
        1,
    );
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
    super::skill::stop(&server, &skill, &WorldObject::from(&player), 0);
    assert!(skill.cancelled());
    client.drain();
    player.data().unit.skill_last_used_ms = 0;
    player.data().unit.global_cooldown_ms = 0;
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
    assert!(!skill.cancelled());
    let deadline = tokio::time::Instant::now() + Duration::from_secs(1);
    while skill.tl_id() != 0 && tokio::time::Instant::now() < deadline {
        tokio::time::sleep(Duration::from_millis(5)).await;
    }
    assert_eq!(npc.data().unit.hp, 900);
    super::skill::end_skill(&server, &skill, &WorldObject::from(&player));
    assert_eq!(
        client
            .drain()
            .iter()
            .filter(|(opcode, _)| *opcode == crate::opcodes::sc::SCSkillEndedPacket)
            .count(),
        1
    );
}

#[tokio::test]
async fn canceled_delayed_effects_cannot_damage_or_finish_twice() {
    let mut data = GameData::empty();
    data.systems.skills.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
        1,
    );
    let (server, mut client, player, npc) = fixture(data);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            effect_delay: 30,
            effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
            ..super::tests::skill_template(100)
        }),
        1,
    );
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
    super::skill::stop(&server, &skill, &WorldObject::from(&player), 0);
    super::skill::stop(&server, &skill, &WorldObject::from(&player), 0);
    tokio::time::sleep(Duration::from_millis(60)).await;
    assert_eq!(npc.data().unit.hp, 1000);
    assert_eq!(
        client
            .drain()
            .iter()
            .filter(|(opcode, _)| *opcode == crate::opcodes::sc::SCSkillEndedPacket)
            .count(),
        1
    );
}

#[tokio::test]
async fn delayed_effects_release_the_timeline_when_caster_or_target_is_despawned() {
    for remove_caster in [false, true] {
        let mut data = GameData::empty();
        data.systems.skills.add_effect(
            EffectKind::DamageEffect,
            EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
            1,
        );
        let (server, _client, player, npc) = fixture(data);
        let skill = SkillRun::new(
            Arc::new(SkillTemplate {
                effect_delay: 30,
                effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
                ..super::tests::skill_template(100)
            }),
            1,
        );
        assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
        let timeline = skill.tl_id();
        let world = server.world.world(0).unwrap();
        let removed = if remove_caster {
            WorldObject::from(&player)
        } else {
            WorldObject::from(&npc)
        };
        world.delete(&removed);
        let mut replacement = Npc::new(2);
        replacement.unit.hp = 777;
        replacement.unit.transform.set_xyz(100.0, 105.0, 100.0);
        let replacement = Entity::new(removed.obj_id(), replacement);
        world.spawn(&replacement, &server.view_ctx());
        tokio::time::sleep(Duration::from_millis(60)).await;
        assert_eq!(npc.data().unit.hp, 1000);
        assert_eq!(replacement.data().unit.hp, 777);
        assert!(skill.cancelled());
        assert_eq!(skill.tl_id(), 0);
        assert!(!server.systems.skills.tl_ids.is_assigned(timeline));
    }
}

#[tokio::test]
async fn channels_stop_on_despawn_without_spending_mana_or_removing_a_replacements_buff() {
    for tick in [0, 10] {
        let mut data = GameData::empty();
        for id in [1, 2] {
            data.systems.skills.add_buff(BuffTemplate {
                id,
                kind: 1,
                duration: 1000,
                ..Default::default()
            });
        }
        data.systems.skills.add_effect(
            EffectKind::DamageEffect,
            EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
            1,
        );
        let (server, _client, player, npc) = fixture(data);
        let skill = SkillRun::new(
            Arc::new(SkillTemplate {
                channeling_time: 60,
                channeling_tick: tick,
                channeling_mana: 7,
                channeling_buff_id: 1,
                channeling_target_buff_id: 2,
                effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
                ..super::tests::skill_template(100)
            }),
            1,
        );
        assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
        let mp = player.data().unit.mp;
        let world = server.world.world(0).unwrap();
        world.delete(&npc);
        let mut replacement = Npc::new(2);
        replacement.unit.hp = 777;
        replacement.unit.max_hp = 777;
        replacement.unit.transform.set_xyz(100.0, 105.0, 100.0);
        let replacement = Entity::new(npc.obj_id, replacement);
        world.spawn(&replacement, &server.view_ctx());
        let target = WorldObject::from(&replacement);
        super::buffs::apply_template(
            &server,
            server.game_data.systems.skills.buff(2).unwrap(),
            Some(&target),
            &SkillCaster::Unit(target.obj_id()),
            &target,
            Some(&skill),
        );
        tokio::time::sleep(Duration::from_millis(100)).await;
        assert!(skill.cancelled());
        assert_eq!(skill.tl_id(), 0);
        assert!(player.data().unit.skill_state.skill_task.is_none());
        assert_eq!(player.data().unit.mp, mp);
        assert!(!player.data().unit.buffs.check_buff(1));
        assert!(replacement.data().unit.buffs.check_buff(2));
        assert_eq!(npc.data().unit.hp, 1000);
        assert_eq!(replacement.data().unit.hp, 777);
    }
}

#[tokio::test]
async fn labor_and_reagents_are_checked_before_cast_mutations() {
    struct Items;
    impl super::ext::ItemSkillHooks for Items {
        fn item_count(&self, _server: &crate::GameServer, _player: &PlayerRef, id: u32) -> i32 {
            if id == 55 {
                2
            } else {
                0
            }
        }
    }
    let mut data = GameData::empty();
    data.systems.skills.skill_reagents = vec![
        super::data::SkillItemAmount {
            id: 1,
            skill_id: 100,
            item_id: 54,
            amount: 1,
        },
        super::data::SkillItemAmount {
            id: 2,
            skill_id: 100,
            item_id: 55,
            amount: 2,
        },
    ];
    let (server, _client, player, npc) = fixture(data);
    server.systems.skills.ext.items.install(Arc::new(Items));
    player.data().labor_power = 4;
    let template = SkillTemplate {
        consume_labor_power: 5,
        ..super::tests::skill_template(100)
    };
    let skill = SkillRun::new(Arc::new(template.clone()), 1);
    assert_eq!(
        cast(&server, &skill, &player, &npc).0,
        skill_result::NEED_LABOR_POWER
    );
    assert_eq!(player.data().unit.mp, 100);
    assert_eq!(player.data().unit.skill_last_used_ms, 0);
    assert_eq!(skill.tl_id(), 0);
    player.data().labor_power = 5;
    assert_eq!(
        cast(&server, &skill, &player, &npc).0,
        skill_result::NEED_REAGENT
    );
    assert_eq!(player.data().labor_power, 5);
    assert_eq!(player.data().unit.skill_last_used_ms, 0);
    let alternate = SkillRun::new(
        Arc::new(SkillTemplate {
            first_reagent_only: true,
            ..template
        }),
        1,
    );
    assert!(
        super::validation::check_resources(&server, &alternate, &WorldObject::from(&player))
            .is_ok()
    );
}

#[tokio::test]
async fn mana_drained_during_cast_prevents_the_skill_from_firing() {
    let mut data = GameData::empty();
    data.systems.skills.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
        1,
    );
    let (server, _client, player, npc) = fixture(data);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            casting_time: 30,
            mana_cost: 25,
            effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
            ..super::tests::skill_template(100)
        }),
        1,
    );
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
    player.data().unit.mp = 20;
    let deadline = tokio::time::Instant::now() + Duration::from_secs(1);
    while skill.tl_id() != 0 && tokio::time::Instant::now() < deadline {
        tokio::time::sleep(Duration::from_millis(5)).await;
    }
    assert!(skill.cancelled());
    assert_eq!(player.data().unit.mp, 20);
    assert_eq!(npc.data().unit.hp, 1000);
}

#[tokio::test]
async fn a_source_dying_during_cast_cannot_spend_mana_or_apply_damage() {
    let (server, _client, player, npc) = fixture(GameData::empty());
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            source_alive: true,
            casting_time: 50,
            mana_cost: 25,
            ..super::tests::skill_template(1)
        }),
        1,
    );
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
    player.data().unit.hp = 0;
    let deadline = tokio::time::Instant::now() + Duration::from_secs(1);
    while !skill.cancelled() && tokio::time::Instant::now() < deadline {
        tokio::time::sleep(Duration::from_millis(5)).await;
    }
    assert!(skill.cancelled());
    assert_eq!(player.data().unit.mp, 100);
    assert_eq!(skill.tl_id(), 0);
    assert_eq!(npc.data().unit.hp, 1000);
}

#[tokio::test]
async fn obstacle_checks_reject_a_physical_wall_before_any_cast_mutation() {
    use crate::systems::ai::collision::models::{ModelPart, ModelPhys};
    use crate::systems::ai::collision::world::{CellContent, StaticInstance};
    use aaemu_physics::geometry::primitives::{BoxPrim, Primitive};
    use aaemu_physics::math::{Quat, Vec3};
    struct Ground;
    impl crate::systems::ai::collision::terrain::TerrainSource for Ground {
        fn sample(&self, _x: i32, _y: i32) -> f32 {
            100.1 // A small terrain/client height difference must not block a ground anchor.
        }
        fn size(&self) -> (i32, i32) {
            (1024, 1024)
        }
    }
    let (server, _client, player, npc) = fixture(GameData::empty());
    server
        .systems
        .ai
        .collision
        .test_files
        .set(Arc::new(std::collections::HashMap::new()))
        .unwrap();
    server
        .systems
        .ai
        .collision
        .test_terrain
        .set(Arc::new(Ground))
        .unwrap_or_else(|_| panic!("terrain"));
    let physics = crate::systems::ai::collision::phys_world(&server, 0).unwrap();
    let mut crop = crate::models::doodad::Doodad::new(1);
    crop.transform.set_xyz(100.0, 102.0, 100.0);
    let crop = Entity::new(0x300, crop);
    let crop_target = Target::Obj(WorldObject::from(&crop));
    let crop_skill = SkillRun::new(
        Arc::new(SkillTemplate {
            check_obstacle: true,
            target_type: super::types::SkillTargetType::Doodad as i32,
            ..super::tests::skill_template(3)
        }),
        1,
    );
    let geometry = physics
        .lock()
        .world
        .register_primitive(&Primitive::Box(BoxPrim::aligned(
            Vec3::ZERO,
            Vec3::new(1.0, 0.1, 1.0),
        )))
        .unwrap();
    physics.lock().insert_cell(CellContent {
        cell: (0, 0),
        instances: vec![StaticInstance {
            model: Arc::new(ModelPhys {
                path: "test/cast-wall.cgf".into(),
                parts: vec![ModelPart {
                    slot: 0,
                    geom: geometry,
                }],
                blob_bytes: 0,
            }),
            pos: Vec3::new(100.0, 102.5, 101.0),
            q: Quat::IDENTITY,
            scale: 1.0,
            flags_default: 0x18fff,
            density: 0.0,
            ent_flags_or: 0x8000,
            vegetation: false,
        }],
        ..Default::default()
    });
    assert!(
        super::validation::check(
            &server,
            &crop_skill,
            &WorldObject::from(&player),
            &crop_target
        )
        .is_ok(),
        "terrain beneath a crop must not block harvesting"
    );
    crop.data().transform.set_xyz(100.0, 105.0, 100.0);
    assert_eq!(
        super::validation::check(
            &server,
            &crop_skill,
            &WorldObject::from(&player),
            &crop_target
        )
        .unwrap_err()
        .0,
        skill_result::OBSTACLE,
        "walls still block doodad interactions"
    );
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            check_obstacle: true,
            mana_cost: 25,
            ..super::tests::skill_template(1)
        }),
        1,
    );
    assert_eq!(
        cast(&server, &skill, &player, &npc).0,
        super::unit_reqs::skill_result_for_key("skill_obstacle")
    );
    assert_eq!(player.data().unit.mp, 100);
    assert_eq!(player.data().unit.skill_last_used_ms, 0);
    assert_eq!(skill.tl_id(), 0);
    // Disabling the configured check retains the explicit template behavior.
    let skill = SkillRun::new(Arc::new(super::tests::skill_template(2)), 1);
    assert_eq!(cast(&server, &skill, &player, &npc).0, 0);
}

#[tokio::test]
async fn insufficient_mana_keeps_ongoing_buffs_and_cast_timing_untouched() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        id: 1,
        remove_on_start_skill: true,
        ..Default::default()
    });
    let (server, _client, player, npc) = fixture(data);
    let mut buff = super::buffs::new_buff(
        &server,
        server.game_data.systems.skills.buff(1).unwrap(),
        Some(&WorldObject::from(&player)),
        &SkillCaster::Unit(player.obj_id),
        None,
        crate::world::now_ms(),
    );
    buff.state = crate::models::buff::effect_state::ACTING;
    player.data().unit.buffs.effects.push(buff);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            mana_cost: 101,
            cancel_ongoing_buffs: true,
            ..super::tests::skill_template(100)
        }),
        1,
    );
    assert_eq!(
        cast(&server, &skill, &player, &npc).0,
        skill_result::LACK_MANA
    );
    assert!(player.data().unit.buffs.check_buff(1));
    assert_eq!(player.data().unit.skill_last_used_ms, 0);
    assert_eq!(player.data().unit.global_cooldown_ms, 0);
    assert_eq!(skill.tl_id(), 0);
}

#[tokio::test]
async fn a_new_cast_obeys_and_cleans_up_the_active_channel_policy() {
    for can_stop in [false, true] {
        let (server, _client, player, npc) = fixture(GameData::empty());
        let channel = SkillRun::new(
            Arc::new(SkillTemplate {
                channeling_time: 500,
                stop_channeling_on_start_skill: can_stop,
                ..super::tests::skill_template(100)
            }),
            1,
        );
        assert_eq!(cast(&server, &channel, &player, &npc).0, 0);
        let skill = SkillRun::new(Arc::new(super::tests::skill_template(101)), 1);
        let result = use_skill(
            &server,
            &skill,
            &WorldObject::from(&player),
            SkillCaster::Unit(player.obj_id),
            &mut SkillCastTarget::Unit(npc.obj_id),
            None,
            true,
        )
        .0;
        if can_stop {
            assert_eq!(result, 0);
            assert!(channel.cancelled());
            assert_eq!(channel.tl_id(), 0);
            assert!(player.data().unit.skill_state.skill_task.is_none());
        } else {
            assert_eq!(
                result,
                super::unit_reqs::skill_result_for_key("skill_cannot_cast_in_channeling")
            );
            assert!(!channel.cancelled());
            assert!(player.data().unit.skill_state.skill_task.is_some());
        }
    }
}
