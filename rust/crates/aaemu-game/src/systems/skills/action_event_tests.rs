use std::sync::Arc;

use crate::data::GameData;
use crate::models::mate::Mate;
use crate::models::movement::MoveTypeActorFlags;
use crate::models::npc::Npc;
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};
use crate::world::{now_ms, Entity, WorldObject};

use super::cast_validation_tests::fixture;
use super::data::effects::BuffEffect;
use super::data::{
    BuffTemplate, BuffTriggerTemplate, EffectKind, EffectTemplate, SkillTemplate, UnitReq,
};
use super::effects::{EffectArgs, EffectSource};
use super::geometry::Target;
use super::state::{Aggro, AggroKind, SkillRun};
use super::types::{
    BuffEventTriggerKind, BuffRemoveOn, CastAction, SkillCastTarget, SkillCaster, SkillObject,
};

fn buff(id: u32) -> BuffTemplate {
    BuffTemplate {
        id,
        kind: 1,
        duration: 10_000,
        bonuses: vec![BonusTemplate {
            attribute: UnitAttribute::MaxHealth,
            modifier_type: UnitModifierType::Value,
            value: 1000,
            linear_level_bonus: 0,
        }],
        ..Default::default()
    }
}

fn args(caster: &WorldObject, target: &WorldObject) -> EffectArgs<'static> {
    EffectArgs {
        caster: Some(caster.clone()),
        caster_caster: SkillCaster::Unit(caster.obj_id()),
        target: Target::Obj(target.clone()),
        target_caster: SkillCastTarget::Unit(target.obj_id()),
        cast: CastAction::Skill {
            skill_id: 1,
            tl_id: 1,
        },
        source: EffectSource::default(),
        skill_object: &SkillObject::None,
    }
}

fn execute(server: &crate::GameServer, kind: i32, args: &EffectArgs<'_>, values: [i32; 4]) {
    super::effects::special::execute(server, kind, args, values);
}

#[tokio::test]
async fn delayed_skill_use_requires_the_original_caster_and_target_handles() {
    for (remove_caster, remove_target, cast_change) in [
        (false, false, 0),
        (true, false, 0),
        (false, true, 0),
        (false, false, 1),
        (false, false, 2),
    ] {
        let mut data = GameData::empty();
        data.systems.skills.add_effect(
            EffectKind::SpecialEffect,
            EffectTemplate::Special(super::data::effects::SpecialEffect {
                id: 1,
                special_effect_type_id: 8,
                value1: 0,
                value2: 0,
                value3: 0,
                value4: 0,
            }),
            1,
        );
        data.systems.skills.add_skill(SkillTemplate {
            mana_cost: 10,
            effects: vec![super::tests::skill_effect(1, EffectKind::SpecialEffect, 1)],
            ..super::tests::skill_template(101)
        });
        let (server, _client, player, npc) = fixture(data);
        let caster = WorldObject::from(&player);
        let target = WorldObject::from(&npc);
        npc.data().unit.current_target = player.obj_id;
        let originating_cast = SkillRun::new(Arc::new(super::tests::skill_template(102)), 1);
        let mut effect_args = args(&caster, &target);
        effect_args.source = EffectSource::from_skill(originating_cast.clone());
        execute(&server, 33, &effect_args, [101, 20, 100, 0]);
        if cast_change == 1 {
            originating_cast.state().cancelled = true;
        } else if cast_change == 2 {
            assert_eq!(
                super::skill::use_skill(
                    &server,
                    &originating_cast,
                    &caster,
                    SkillCaster::Unit(player.obj_id),
                    &mut SkillCastTarget::Unit(npc.obj_id),
                    Some(SkillObject::None),
                    true,
                )
                .0,
                super::types::skill_result::SUCCESS
            );
        }
        let world = server.world.world(0).unwrap();
        let replaced = if remove_caster {
            Some(&caster)
        } else if remove_target {
            Some(&target)
        } else {
            None
        };
        let replacement = replaced.map(|object| {
            world.delete(object);
            let mut n = Npc::new(2);
            n.unit.hp = 1000;
            n.unit.current_target = player.obj_id;
            n.unit.transform.set_xyz(100.0, 105.0, 100.0);
            let n = Entity::new(object.obj_id(), n);
            world.spawn(&n, &server.view_ctx());
            n
        });
        tokio::time::sleep(std::time::Duration::from_millis(60)).await;
        let blocked = replaced.is_some() || cast_change != 0;
        assert_eq!(player.data().unit.mp, if blocked { 100 } else { 90 });
        assert_eq!(
            npc.data().unit.current_target,
            if blocked { player.obj_id } else { 0 }
        );
        if let Some(n) = replacement {
            assert_eq!(n.data().unit.current_target, player.obj_id);
        }
    }
}

#[tokio::test]
async fn delayed_disturbance_does_not_interrupt_a_removed_or_recycled_target() {
    for remove_target in [false, true] {
        let (server, _client, player, npc) = fixture(GameData::empty());
        let owner = WorldObject::from(&npc);
        let skill = SkillRun::new(Arc::new(super::tests::skill_template(1)), 1);
        skill.state().tl_id = server.systems.skills.tl_ids.next_id();
        npc.data().unit.skill_state.skill_task = Some(super::state::SkillTaskSlot {
            kind: super::state::SkillTaskKind::Cast,
            skill: skill.clone(),
            cancel: super::state::CancelToken::new(),
        });
        execute(
            &server,
            5,
            &args(&WorldObject::from(&player), &owner),
            [100, 20, 0, 0],
        );
        if remove_target {
            let world = server.world.world(0).unwrap();
            world.delete(&owner);
            let mut n = Npc::new(2);
            n.unit.hp = 1000;
            world.spawn(Entity::new(npc.obj_id, n), &server.view_ctx());
        }
        tokio::time::sleep(std::time::Duration::from_millis(60)).await;
        assert_eq!(skill.cancelled(), !remove_target);
        assert_eq!(
            npc.data().unit.skill_state.skill_task.is_some(),
            remove_target
        );
    }
}
#[tokio::test]
async fn repeated_special_actions_do_not_mutate_targets_after_cancel_or_skill_reuse() {
    for (cancel, reuse, despawn) in [
        (false, false, false),
        (true, false, false),
        (false, true, false),
        (false, false, true),
    ] {
        let (server, _client, player, npc) = fixture(GameData::empty());
        let caster = WorldObject::from(&player);
        let target = WorldObject::from(&npc);
        let skill = SkillRun::new(
            Arc::new(SkillTemplate {
                effect_repeat_count: 3,
                effect_repeat_tick: 10,
                ..super::tests::skill_template(100)
            }),
            1,
        );
        let mut effect_args = args(&caster, &target);
        effect_args.source = EffectSource::from_skill(skill.clone());
        npc.data().unit.current_target = player.obj_id;
        super::effects::special::apply(
            &server,
            &super::data::effects::SpecialEffect {
                id: 1,
                special_effect_type_id: 8,
                value1: 0,
                value2: 0,
                value3: 0,
                value4: 0,
            },
            &effect_args,
        );
        assert_eq!(npc.data().unit.current_target, 0);
        if reuse {
            assert_eq!(
                super::skill::use_skill(
                    &server,
                    &skill,
                    &caster,
                    SkillCaster::Unit(player.obj_id),
                    &mut SkillCastTarget::Unit(npc.obj_id),
                    Some(SkillObject::None),
                    true,
                )
                .0,
                super::types::skill_result::SUCCESS
            );
        } else if cancel {
            skill.state().cancelled = true;
        }
        if despawn {
            let world = server.world.world(0).unwrap();
            world.delete(&target);
            let mut replacement = Npc::new(2);
            replacement.unit.hp = 1000;
            replacement.unit.transform.set_xyz(100.0, 105.0, 100.0);
            world.spawn(Entity::new(npc.obj_id, replacement), &server.view_ctx());
        }
        npc.data().unit.current_target = player.obj_id;
        tokio::time::sleep(std::time::Duration::from_millis(40)).await;
        assert_eq!(
            npc.data().unit.current_target,
            if cancel || reuse || despawn {
                player.obj_id
            } else {
                0
            }
        );
    }
}

#[tokio::test]
async fn steal_transfers_only_an_eligible_buff_and_preserves_remaining_duration() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(buff(1));
    data.systems
        .skills
        .add_buff(BuffTemplate { kind: 2, ..buff(2) });
    data.systems.skills.add_buff(BuffTemplate {
        system: true,
        ..buff(3)
    });
    let (server, _client, player, npc) = fixture(data);
    let caster = WorldObject::from(&player);
    let target = WorldObject::from(&npc);
    for id in [1, 2, 3] {
        super::buffs::add_buff_by_id(&server, &target, id, &target, 0);
    }
    target.with_base_unit(|unit| {
        let b = unit
            .buffs_mut()
            .effects
            .iter_mut()
            .find(|b| b.template.id == 1)
            .unwrap();
        b.start_time_ms -= 2000;
        b.charge = 3;
        b.ab_level = 7;
    });
    execute(&server, 16, &args(&caster, &target), [1, 0, 0, 0]);
    let stolen = super::combat::buff_of(&caster, 1).expect("stolen buff");
    assert!(stolen.time_left(now_ms()) > 7000.0);
    assert!(stolen.duration <= 8000);
    assert_eq!(stolen.charge, 3);
    assert_eq!(stolen.ab_level, 7);
    assert_eq!(stolen.caster_obj_id, caster.obj_id());
    assert!(!target.with_base_unit(|u| u.buffs().check_buff(1)));
    for id in [2, 3] {
        assert!(target.with_base_unit(|u| u.buffs().check_buff(id)));
        assert!(!caster.with_base_unit(|u| u.buffs().check_buff(id)));
    }
}

#[tokio::test]
async fn aggro_reset_selects_components_and_copy_registers_watchers() {
    let (server, _client, player, npc) = fixture(GameData::empty());
    let player_obj = WorldObject::from(&player);
    let source = WorldObject::from(&npc);
    let mut n = Npc::new(2);
    n.unit.hp = 1000;
    n.unit.transform.set_xyz(105.0, 100.0, 100.0);
    let other = Entity::new(0x300, n);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&other, &server.view_ctx());
    let target = WorldObject::from(&other);
    super::combat::add_unit_aggro(&server, &source, AggroKind::Damage, &player_obj, 100);
    super::combat::add_unit_aggro(&server, &source, AggroKind::Heal, &player_obj, 50);
    super::combat::add_unit_aggro(&server, &source, AggroKind::Etc, &player_obj, 7);
    execute(&server, 105, &args(&player_obj, &source), [0, 1, 0, 0]);
    assert_eq!(
        npc.data().unit.skill_state.aggro_table[&player.obj_id],
        Aggro {
            damage_aggro: 100,
            heal_aggro: 0,
            etc_aggro: 7,
        }
    );
    execute(&server, 104, &args(&source, &target), [0; 4]);
    let expected = npc.data().unit.skill_state.aggro_table.clone();
    assert_eq!(other.data().unit.skill_state.aggro_table, expected);
    assert!(player
        .data()
        .skill_char
        .is_in_aggro_list_of
        .contains(&other.obj_id));
    execute(&server, 105, &args(&player_obj, &source), [0; 4]);
    assert!(npc.data().unit.skill_state.aggro_table.is_empty());
    assert!(!player
        .data()
        .skill_char
        .is_in_aggro_list_of
        .contains(&npc.obj_id));
    // Native copy is a no-op when the source table is empty.
    execute(&server, 104, &args(&source, &target), [0; 4]);
    assert!(!other.data().unit.skill_state.aggro_table.is_empty());
}

#[tokio::test]
async fn fake_death_removes_incoming_aggro_without_death_side_effects() {
    let (server, _client, player, npc) = fixture(GameData::empty());
    let target = WorldObject::from(&player);
    let holder = WorldObject::from(&npc);
    super::combat::add_unit_aggro(&server, &holder, AggroKind::Damage, &target, 100);
    super::combat::add_unit_aggro(&server, &target, AggroKind::Damage, &holder, 25);
    let hp = player.data().unit.hp;
    let outgoing = player.data().unit.skill_state.aggro_table.clone();
    execute(&server, 18, &args(&target, &target), [700, 70, 0, 0]);
    assert!(npc.data().unit.skill_state.aggro_table.is_empty());
    assert!(player.data().skill_char.is_in_aggro_list_of.is_empty());
    assert_eq!(player.data().unit.hp, hp);
    assert_eq!(player.data().unit.skill_state.aggro_table, outgoing);
    assert!(!server
        .systems
        .skills
        .aggro_watch
        .lock()
        .unwrap()
        .contains_key(&player.obj_id));
    assert!(server.world.object(player.obj_id).is_some());
}

#[tokio::test]
async fn losing_target_canceling_buffs_and_despawning_change_live_state() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        remove_on_start_skill: true,
        ..buff(1)
    });
    data.systems.skills.add_buff(buff(2));
    let (server, _client, player, npc) = fixture(data);
    let caster = WorldObject::from(&player);
    let target = WorldObject::from(&npc);
    npc.data().unit.current_target = player.obj_id;
    execute(&server, 8, &args(&caster, &target), [0; 4]);
    assert_eq!(npc.data().unit.current_target, 0);
    for id in [1, 2] {
        super::buffs::add_buff_by_id(&server, &target, id, &target, 0);
    }
    execute(&server, 61, &args(&caster, &target), [0; 4]);
    assert!(!npc.data().unit.buffs.check_buff(1));
    assert!(npc.data().unit.buffs.check_buff(2));
    execute(&server, 61, &args(&caster, &target), [2, 0, 0, 0]);
    assert!(!npc.data().unit.buffs.check_buff(2));
    execute(&server, 109, &args(&caster, &target), [0; 4]);
    assert!(server.world.object(npc.obj_id).is_none());
}

fn event_data(kind: BuffEventTriggerKind, remove: Option<BuffRemoveOn>) -> GameData {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        remove_on_move: remove == Some(BuffRemoveOn::Move),
        remove_on_damaged_etc: remove == Some(BuffRemoveOn::DamagedEtc),
        ..buff(1)
    });
    data.systems.skills.add_buff(buff(2));
    data.systems.skills.add_effect(
        EffectKind::BuffEffect,
        EffectTemplate::Buff(BuffEffect {
            id: 2,
            buff_id: Some(2),
            chance: 100,
            stack: 1,
            ab_level: 0,
        }),
        0,
    );
    data.systems.skills.buff_triggers.insert(
        1,
        vec![BuffTriggerTemplate {
            id: 1,
            kind: kind as u16,
            effect_on_source: false,
            effect: (EffectKind::BuffEffect, 2),
            use_damage_amount: false,
            use_original_source: false,
            target_buff_tag_id: 0,
            target_no_buff_tag_id: 0,
            synergy: false,
        }],
    );
    data
}

#[tokio::test]
async fn unit_events_follow_the_buff_owner_and_removal_events_fire_before_disposal() {
    for kind in [
        BuffEventTriggerKind::Attacked,
        BuffEventTriggerKind::Landing,
        BuffEventTriggerKind::ChannelingCancel,
    ] {
        let (server, _client, player, npc) = fixture(event_data(kind, None));
        let caster = WorldObject::from(&player);
        let owner = WorldObject::from(&npc);
        super::buffs::add_buff_by_id(&server, &owner, 1, &caster, 0);
        super::buffs::fire_unit_event(&server, caster.obj_id(), kind, 0);
        assert!(!npc.data().unit.buffs.check_buff(2));
        super::buffs::fire_unit_event(&server, owner.obj_id(), kind, 0);
        assert!(npc.data().unit.buffs.check_buff(2), "event {kind:?}");
    }
    for (kind, remove) in [
        (BuffEventTriggerKind::RemoveOnMove, BuffRemoveOn::Move),
        (
            BuffEventTriggerKind::RemoveOnDamage,
            BuffRemoveOn::DamagedEtc,
        ),
    ] {
        let (server, _client, player, npc) = fixture(event_data(kind, Some(remove)));
        let owner = WorldObject::from(&npc);
        super::buffs::add_buff_by_id(&server, &owner, 1, &WorldObject::from(&player), 0);
        super::buffs::trigger_remove_on(&server, &owner, remove, 0);
        assert!(!npc.data().unit.buffs.check_buff(1));
        assert!(npc.data().unit.buffs.check_buff(2));
        assert!(!server
            .systems
            .skills
            .triggers
            .lock()
            .unwrap()
            .iter()
            .any(|s| s.buff_index == 2));
    }
}

#[tokio::test]
async fn short_jump_landing_does_not_require_a_damage_velocity() {
    let (server, _client, player, _npc) = fixture(event_data(BuffEventTriggerKind::Landing, None));
    let owner = WorldObject::from(&player);
    super::buffs::add_buff_by_id(&server, &owner, 1, &owner, 0);
    super::on_actor_move(&server, &owner, MoveTypeActorFlags::JUMPING, 0);
    assert!(!player.data().unit.buffs.check_buff(2));
    super::on_actor_move(&server, &owner, MoveTypeActorFlags::STANDING_ON_SOLID, 0);
    assert!(player.data().unit.buffs.check_buff(2));
    super::buffs::remove_buff(&server, &owner, 2);
    super::on_actor_move(&server, &owner, MoveTypeActorFlags::STANDING_ON_SOLID, 0);
    assert!(!player.data().unit.buffs.check_buff(2));
}

#[tokio::test]
async fn an_equip_item_requirement_cannot_be_satisfied_by_an_item_in_the_bag() {
    let (server, _client, player, _npc) = fixture(GameData::empty());
    let owner = WorldObject::from(&player);
    let requirement = UnitReq {
        id: 1,
        owner_id: 1,
        owner_type: "Skill".into(),
        kind: 9,
        value1: 55,
        value2: 0,
    };
    let (bag, equipment) = {
        let player = player.data();
        (
            player
                .inventory
                .get(crate::models::container::slot_type::INVENTORY)
                .unwrap()
                .clone(),
            player.inventory.equipment().clone(),
        )
    };
    let mut item = crate::models::item::Item::new(1);
    item.id = 1;
    item.template_id = 55;
    item.count = 1;
    bag.lock().unwrap().items.push(item);
    assert!(!super::unit_reqs::validate(&server, &requirement, &owner, None).is_ok());
    let item = bag.lock().unwrap().items.pop().unwrap();
    equipment.lock().unwrap().items.push(item);
    assert!(super::unit_reqs::validate(&server, &requirement, &owner, None).is_ok());
}

#[tokio::test]
async fn requirements_check_the_packet_doodad_and_resource_boundaries() {
    let mut data = GameData::empty();
    data.systems.quests.group_npcs.add(55, 1);
    let (server, _client, player, npc) = fixture(data);
    let owner = WorldObject::from(&player);
    let doodad = Entity::new(0x500, crate::models::doodad::Doodad::new(55));
    let target = WorldObject::from(&doodad);
    let mut requirement = UnitReq {
        id: 1,
        owner_id: 1,
        owner_type: "Skill".into(),
        kind: 0x1c,
        value1: 55,
        value2: 0,
    };
    player.data().unit.current_target = npc.obj_id;
    assert!(super::unit_reqs::validate(&server, &requirement, &owner, Some(&target)).is_ok());
    assert!(!super::unit_reqs::validate(
        &server,
        &requirement,
        &owner,
        Some(&WorldObject::from(&npc))
    )
    .is_ok());
    requirement.kind = 0x22;
    assert!(super::unit_reqs::validate(
        &server,
        &requirement,
        &owner,
        Some(&WorldObject::from(&npc))
    )
    .is_ok());
    requirement.value1 = 56;
    assert!(!super::unit_reqs::validate(
        &server,
        &requirement,
        &owner,
        Some(&WorldObject::from(&npc))
    )
    .is_ok());
    requirement.kind = 0x29;
    requirement.value1 = 2000;
    requirement.value2 = 600;
    for (hour, accepted) in [
        (20.0, true),
        (23.0, true),
        (0.0, true),
        (6.0, true),
        (6.5, false),
        (19.99, false),
    ] {
        server.time.set(hour);
        assert_eq!(
            super::unit_reqs::validate(&server, &requirement, &owner, None).is_ok(),
            accepted
        );
    }
    requirement.kind = 0x2c;
    requirement.value1 = 2;
    requirement.value2 = 9;
    for (points, accepted) in [(1, false), (2, true), (9, true), (10, false)] {
        player.data().crime_point = points;
        assert_eq!(
            super::unit_reqs::validate(&server, &requirement, &owner, None).is_ok(),
            accepted
        );
    }
    requirement.value1 = 20;
    for kind in [0x41, 0x42] {
        requirement.kind = kind;
        {
            let mut player = player.data();
            player.unit.max_hp = 100;
            player.unit.max_mp = 100;
            player.unit.hp = 80;
            player.unit.mp = 80;
        }
        assert!(super::unit_reqs::validate(&server, &requirement, &owner, None).is_ok());
        requirement.value1 = 21;
        assert!(!super::unit_reqs::validate(&server, &requirement, &owner, None).is_ok());
        requirement.value1 = 20;
    }
}

#[tokio::test]
async fn resolved_packet_target_is_used_for_unit_requirements() {
    let mut data = GameData::empty();
    data.systems.skills.unit_reqs.insert(
        "Skill".into(),
        vec![UnitReq {
            id: 1,
            owner_id: 1,
            owner_type: "Skill".into(),
            kind: 0x1b,
            value1: 1,
            value2: 0,
        }],
    );
    let (server, _client, player, npc) = fixture(data);
    player.data().unit.current_target = 0;
    let skill = SkillRun::new(Arc::new(super::tests::skill_template(1)), 1);
    assert_eq!(
        super::skill::use_skill(
            &server,
            &skill,
            &WorldObject::from(&player),
            SkillCaster::Unit(player.obj_id),
            &mut SkillCastTarget::Unit(npc.obj_id),
            None,
            false
        )
        .0,
        0
    );
}

#[tokio::test]
async fn direct_area_damage_respects_the_caster_cone() {
    let mut data = GameData::empty();
    data.systems.skills.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(super::tests::damage_effect(1, 100))),
        1,
    );
    let (server, _client, player, front) = fixture(data);
    let mut others = Vec::new();
    for (id, x, y) in [(0x300, 100.0, 95.0), (0x400, 105.0, 100.0)] {
        let mut n = Npc::new(1);
        n.unit.hp = 1000;
        n.unit.max_hp = 1000;
        n.unit.transform.set_xyz(x, y, 100.0);
        let npc = Entity::new(id, n);
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&npc, &server.view_ctx());
        others.push(npc);
    }
    let caster = WorldObject::from(&player);
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            target_area_radius: 10,
            target_area_angle: 90,
            target_area_count: 10,
            target_type: 0,
            target_selection: 1,
            effects: vec![super::tests::skill_effect(1, EffectKind::DamageEffect, 1)],
            ..super::tests::skill_template(1)
        }),
        1,
    );
    super::skill::apply_effects(
        &server,
        &skill,
        &super::skill::CastCtx {
            caster: caster.clone(),
            caster_caster: SkillCaster::Unit(caster.obj_id()),
            target: Target::Obj(caster.clone()),
            target_caster: SkillCastTarget::Unit(caster.obj_id()),
            skill_object: SkillObject::None,
        },
    );
    assert_eq!(front.data().unit.hp, 900);
    for npc in others {
        assert_eq!(npc.data().unit.hp, 1000);
    }
}

#[tokio::test]
async fn pet_recovery_restores_percentages_clamps_and_does_not_resurrect() {
    let (server, _client, player, _npc) = fixture(GameData::empty());
    let mut mate = Mate::new(1, 1);
    mate.unit.hp = 900;
    mate.unit.max_hp = 1000;
    mate.unit.mp = 100;
    mate.unit.max_mp = 500;
    mate.unit.transform.set_xyz(100.0, 100.0, 100.0);
    let mate = Entity::new(0x300, mate);
    let target = WorldObject::from(&mate);
    let caster = WorldObject::from(&player);
    execute(&server, 56, &args(&caster, &target), [20, 20, 0, 0]);
    {
        let m = mate.data();
        assert_eq!((m.unit.hp, m.unit.mp), (1000, 200));
    }
    mate.data().unit.hp = 0;
    execute(&server, 56, &args(&caster, &target), [20, 20, 0, 0]);
    {
        let m = mate.data();
        assert_eq!((m.unit.hp, m.unit.mp), (0, 200));
    }
}

#[tokio::test]
async fn create_mana_uses_health_spent_and_inspired_doubles_the_conversion() {
    use super::data::effects::ConversionEffect;
    for (ratio, expected) in [(50, 200), (100, 300)] {
        let (server, _client, player, _npc) = fixture(GameData::empty());
        {
            let mut p = player.data();
            p.unit.max_hp = 1000;
            p.unit.max_mp = 1000;
        }
        let caster = WorldObject::from(&player);
        let effect = EffectTemplate::Conversion(ConversionEffect {
            id: 11,
            category_id: 2,
            source_category_id: 2,
            source_value: 20,
            target_category_id: 4,
            target_value: ratio,
        });
        super::effects::apply(
            &server,
            EffectKind::ConversionEffect,
            &effect,
            &args(&caster, &caster),
            None,
        );
        {
            let p = player.data();
            assert_eq!((p.unit.hp, p.unit.mp), (800, expected));
        }
        // "Up to" the health cost: retain the last HP and use the actual amount spent.
        {
            let mut p = player.data();
            p.unit.hp = 11;
            p.unit.mp = 0;
        }
        super::effects::apply(
            &server,
            EffectKind::ConversionEffect,
            &effect,
            &args(&caster, &caster),
            None,
        );
        {
            let p = player.data();
            assert_eq!((p.unit.hp, p.unit.mp), (1, ratio / 10));
        }
    }
}

#[tokio::test]
async fn conversions_support_fixed_costs_and_maximum_resource_percentages() {
    use super::data::effects::ConversionEffect;
    let (server, _client, player, _npc) = fixture(GameData::empty());
    let caster = WorldObject::from(&player);
    {
        let mut p = player.data();
        p.unit.hp = 500;
        p.unit.max_hp = 1000;
        p.unit.max_mp = 1000;
    }
    let effect = EffectTemplate::Conversion(ConversionEffect {
        id: 52,
        category_id: 1,
        source_category_id: 2,
        source_value: 2,
        target_category_id: 2,
        target_value: 1,
    });
    super::effects::apply(
        &server,
        EffectKind::ConversionEffect,
        &effect,
        &args(&caster, &caster),
        None,
    );
    {
        let p = player.data();
        assert_eq!((p.unit.hp, p.unit.mp), (510, 80));
    }
    let effect = EffectTemplate::Conversion(ConversionEffect {
        id: 38,
        category_id: 2,
        source_category_id: 1,
        source_value: 50,
        target_category_id: 1,
        target_value: 0,
    });
    super::effects::apply(
        &server,
        EffectKind::ConversionEffect,
        &effect,
        &args(&caster, &caster),
        None,
    );
    {
        let p = player.data();
        assert_eq!((p.unit.hp, p.unit.mp), (460, 80));
    }
}

#[tokio::test]
async fn charge_refresh_accumulates_caps_and_keeps_one_buff() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        max_charge: 10,
        stack_rule: crate::data::skills::stack_rule::REFRESH,
        ..buff(1)
    });
    let (server, _client, player, _npc) = fixture(data);
    let owner = WorldObject::from(&player);
    for expected in [3, 6, 9, 10] {
        execute(&server, 1, &args(&owner, &owner), [1, 3, 3, 0]);
        let b = super::combat::buff_of(&owner, 1).unwrap();
        assert_eq!(b.charge, expected);
        assert_eq!(
            super::buffs::buff_created_packet(&server, &owner, &b).charge,
            expected
        );
        assert_eq!(
            owner.with_base_unit(|u| u
                .buffs()
                .effects
                .iter()
                .filter(|b| b.template.id == 1)
                .count()),
            1
        );
    }
}

#[tokio::test]
async fn target_charges_use_their_own_buff_and_damage_multiplier() {
    let mut data = GameData::empty();
    for id in [1, 2] {
        data.systems.skills.add_buff(buff(id));
    }
    let (server, _client, player, npc) = fixture(data);
    let caster = WorldObject::from(&player);
    let target = WorldObject::from(&npc);
    for id in [1, 2] {
        super::buffs::add_buff_by_id(&server, &target, id, &target, 0);
    }
    target.with_base_unit(|u| {
        u.buffs_mut()
            .effects
            .iter_mut()
            .find(|b| b.template.id == 2)
            .unwrap()
            .charge = 3
    });
    let skill = SkillRun::new(Arc::new(super::tests::skill_template(1)), 1);
    let mut args = args(&caster, &target);
    args.source = EffectSource::from_skill(skill);
    let mut effect = super::tests::damage_effect(1, 0);
    effect.use_fixed_damage = false;
    effect.use_target_charged_buff = true;
    effect.charged_buff_id = 1;
    effect.charged_mul = 1000.0;
    effect.target_charged_buff_id = 2;
    effect.target_charged_mul = 50.0;
    super::effects::combat::damage(&server, &effect, &args, None);
    assert_eq!(npc.data().unit.hp, 850);
    assert!(npc.data().unit.buffs.check_buff(1));
    assert!(!npc.data().unit.buffs.check_buff(2));
}

#[tokio::test]
async fn edge_distance_scales_both_actor_radii_symmetrically() {
    let mut data = GameData::empty();
    data.systems.skills.model_radii.insert(1, 1.0);
    let (server, _client, player, npc) = fixture(data);
    {
        let mut p = player.data();
        p.unit.model_id = 1;
        p.unit.scale = 2.0;
    }
    {
        let mut n = npc.data();
        n.unit.model_id = 1;
        n.unit.scale = 3.0;
        n.unit.transform.y = 110.0;
    }
    let source = Target::Obj(WorldObject::from(&player));
    let target = Target::Obj(WorldObject::from(&npc));
    assert_eq!(
        super::geometry::distance_to(&server, &source, &target, true),
        5.0
    );
    assert_eq!(
        super::geometry::distance_to(&server, &target, &source, true),
        5.0
    );
}

#[tokio::test]
async fn ambient_buff_effects_still_obey_zero_and_full_probability() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(buff(1));
    let (server, _client, player, npc) = fixture(data);
    let owner = WorldObject::from(&npc);
    let mut args = args(&WorldObject::from(&player), &owner);
    args.caster = None;
    for chance in [0, 100] {
        super::effects::combat::buff_effect(
            &server,
            &BuffEffect {
                id: 1,
                buff_id: Some(1),
                chance,
                stack: 1,
                ab_level: 0,
            },
            &args,
        );
        assert_eq!(npc.data().unit.buffs.check_buff(1), chance == 100);
    }
}

#[tokio::test]
async fn escape_timeout_uses_the_owners_return_point_and_return_preserves_radians() {
    use aaemu_commons::PacketStream;
    struct Portals;
    impl super::ext::NpcSkillHooks for Portals {
        fn closest_return_portal(
            &self,
            _server: &crate::GameServer,
            _player: &crate::world::PlayerRef,
        ) -> Option<crate::data::world_data::Portal> {
            Some(crate::data::world_data::Portal {
                x: 12.0,
                y: 14.0,
                z: 22.0,
                z_rot: 1.1,
                ..Default::default()
            })
        }
    }
    let (server, mut client, player, npc) = fixture(GameData::empty());
    server.systems.skills.ext.npcs.install(Arc::new(Portals));
    let owner = WorldObject::from(&player);
    super::effects::apply(
        &server,
        EffectKind::MoveToRezPointEffect,
        &EffectTemplate::MoveToRezPoint { id: 1 },
        &args(&WorldObject::from(&npc), &owner),
        None,
    );
    let body = client
        .drain()
        .into_iter()
        .find(|(op, _)| *op == crate::opcodes::sc::SCCharacterResurrectedPacket)
        .unwrap()
        .1;
    let mut s = PacketStream::from_bytes(body);
    assert_eq!(s.read_bc().unwrap(), player.obj_id);
    let (x, y, z) = s.read_position().unwrap();
    assert!((x - 12.0).abs() < 0.001 && (y - 14.0).abs() < 0.001 && (z - 22.0).abs() < 0.001);
    assert!((s.read_f32().unwrap() - 1.1).abs() < 0.001);
    player.data().main_world_position = Some(crate::models::transform::Transform {
        instance_id: 0,
        zone_id: 100,
        x: 30.0,
        y: 40.0,
        z: 50.0,
        rot_z: 1.2,
        ..Default::default()
    });
    execute(&server, 25, &args(&owner, &owner), [999, 0, 0, 0]);
    let frames = client.drain();
    let load = frames
        .iter()
        .find(|(op, _)| *op == crate::opcodes::sc::SCLoadInstancePacket)
        .unwrap();
    let mut s = PacketStream::from_bytes(load.1.clone());
    assert_eq!(s.read_u32().unwrap(), 0);
    assert_eq!(s.read_u32().unwrap(), 100);
    for expected in [30.0, 40.0, 50.0, 0.0, 0.0, 1.2] {
        assert!((s.read_f32().unwrap() - expected).abs() < 0.001);
    }
    assert!(frames
        .iter()
        .any(|(op, _)| *op == crate::opcodes::sc::SCTeleportUnitPacket));
    assert_eq!(player.data().unit.transform.rot_z, 1.2);
}

#[tokio::test]
async fn doodad_summons_use_the_selected_frame_and_keep_radian_rotations() {
    use crate::models::transform::PositionAndRotation;
    #[derive(Default)]
    struct Spawns(std::sync::Mutex<Vec<PositionAndRotation>>);
    impl super::ext::NpcSkillHooks for Spawns {
        fn spawn_doodad(
            &self,
            _server: &crate::GameServer,
            _caster: &WorldObject,
            _template_id: u32,
            _zone_id: u32,
            pos: PositionAndRotation,
            _delay: i32,
        ) {
            self.0.lock().unwrap().push(pos);
        }
    }
    let (server, _client, player, npc) = fixture(GameData::empty());
    let spawns = Arc::new(Spawns::default());
    server.systems.skills.ext.npcs.install(spawns.clone());
    player.data().unit.transform.rot_z = std::f32::consts::FRAC_PI_2;
    for selection in [1, 2] {
        let mut args = args(&WorldObject::from(&player), &WorldObject::from(&npc));
        args.source = EffectSource::from_skill(SkillRun::new(
            Arc::new(SkillTemplate {
                target_selection: selection,
                ..Default::default()
            }),
            1,
        ));
        execute(&server, 15, &args, [1, 0, 0, 0]);
    }
    let positions = spawns.0.lock().unwrap();
    assert!((positions[0].x - 99.0).abs() < 0.001 && (positions[0].y - 100.0).abs() < 0.001);
    assert_eq!(positions[0].rot_z, std::f32::consts::FRAC_PI_2);
    assert!((positions[1].x - 100.0).abs() < 0.001 && (positions[1].y - 106.0).abs() < 0.001);
    assert_eq!(positions[1].rot_z, 0.0);
}

#[tokio::test]
async fn cooldown_resets_clear_server_state_for_a_skill_and_tag() {
    let mut data = GameData::empty();
    data.systems.skills.tagged_skills.insert(7, vec![2, 3]);
    let (server, _client, player, _npc) = fixture(data);
    let owner = WorldObject::from(&player);
    {
        let mut p = player.data();
        for id in [1, 2, 3, 4] {
            p.unit.cooldowns.add_cooldown(id, 10_000, now_ms());
        }
        p.unit.global_cooldown_ms = now_ms() + 10_000;
    }
    execute(&server, 43, &args(&owner, &owner), [1, 7, 1, 0]);
    let mut p = player.data();
    assert_eq!(p.unit.global_cooldown_ms, 0);
    for id in [1, 2, 3] {
        assert!(!p.unit.cooldowns.check_cooldown(id, now_ms()));
    }
    assert!(p.unit.cooldowns.check_cooldown(4, now_ms()));
}

#[tokio::test]
async fn disturb_casting_cancels_direct_casts_and_honors_zero_probability() {
    let (server, _client, player, npc) = fixture(GameData::empty());
    let owner = WorldObject::from(&npc);
    let skill = SkillRun::new(Arc::new(super::tests::skill_template(1)), 1);
    skill.state().tl_id = server.systems.skills.tl_ids.next_id();
    npc.data().unit.skill_state.skill_task = Some(super::state::SkillTaskSlot {
        kind: super::state::SkillTaskKind::Cast,
        skill: skill.clone(),
        cancel: super::state::CancelToken::new(),
    });
    let args = args(&WorldObject::from(&player), &owner);
    execute(&server, 5, &args, [0, 0, 0, 0]);
    assert!(npc.data().unit.skill_state.skill_task.is_some());
    execute(&server, 5, &args, [100, 0, 0, 0]);
    assert!(npc.data().unit.skill_state.skill_task.is_none());
    assert!(skill.cancelled());
    assert_eq!(skill.tl_id(), 0);
}

#[tokio::test]
async fn song_healing_and_mana_restoration_use_per_thousand_maximums() {
    use super::data::effects::{HealEffect, RestoreManaEffect};
    let (server, _client, player, npc) = fixture(GameData::empty());
    let owner = WorldObject::from(&npc);
    {
        let mut n = npc.data();
        n.unit.max_hp = 5000;
        n.unit.hp = 2000;
        n.unit.max_mp = 4000;
        n.unit.mp = 1000;
    }
    let args = args(&WorldObject::from(&player), &owner);
    // Real heal77 / song11667 specifies20 and promises2% of the ally's maximum health.
    let effect = HealEffect {
        id: 77,
        use_fixed_heal: true,
        fixed_min: 20,
        fixed_max: 20,
        use_level_heal: false,
        level_md: 0.0,
        level_va_start: 0,
        level_va_end: 0,
        percent: true,
        use_charged_buff: false,
        charged_buff_id: 0,
        charged_mul: 0.0,
        slave_applicable: false,
        ignore_heal_aggro: true,
        dps_multiplier: 0.0,
        actability_group_id: 0,
        actability_step: 0,
        actability_mul: 0.0,
        actability_add: 0.0,
    };
    super::effects::combat::heal(&server, &effect, &args, None);
    assert_eq!(npc.data().unit.hp, 2100);
    super::effects::combat::restore_mana(
        &server,
        &RestoreManaEffect {
            id: 9,
            use_fixed_value: true,
            fixed_min: 1000,
            fixed_max: 1000,
            use_level_value: false,
            level_md: 0.0,
            level_va_start: 0,
            level_va_end: 0,
            percent: true,
        },
        &args,
    );
    assert_eq!(npc.data().unit.mp, 4000);
}
