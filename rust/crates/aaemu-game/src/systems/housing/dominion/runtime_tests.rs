use super::super::state::SiegeSide;
use super::*;
use crate::data::world_data::{SystemFaction, Zone};
use crate::data::GameData;
use crate::models::house::House;
use crate::systems::housing::data::{GuardTowerSetting, HousingTemplate};
use crate::systems::skills::data::{BuffTemplate, SkillTemplate};
use crate::systems::skills::effects::EffectSource;
use crate::systems::skills::skill::CastCtx;
use crate::systems::skills::state::{PlotStateRef, SkillRun};
use crate::systems::skills::types::{SkillCastTarget, SkillObject};
use crate::systems::social::expedition::{member_from_character, Expedition};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{Entity, PlayerRef};

struct Fixture {
    server: GameServer,
    player: PlayerRef,
    house: HouseRef,
    skill: SkillRef,
    plot: Arc<PlotState>,
    handle: EngravingHandle,
}

fn fixture(start_offset_ms: i64) -> Fixture {
    let mut data = GameData::empty();
    data.zones.zones.insert(
        TEST_ZONE,
        Zone {
            zone_key: TEST_ZONE,
            group_id: 25,
            ..Default::default()
        },
    );
    data.systems.housing.add_template(HousingTemplate {
        id: 139,
        guard_tower_setting_id: 6,
        hp: 1000,
        ..Default::default()
    });
    data.systems.housing.castles.guard_tower_settings.insert(
        6,
        GuardTowerSetting {
            id: 6,
            ..Default::default()
        },
    );
    data.systems.skills.skills.insert(
        ENGRAVING_SKILL,
        Arc::new(SkillTemplate {
            id: ENGRAVING_SKILL,
            plot_id: Some(ENGRAVING_PLOT),
            max_range: 30,
            ..Default::default()
        }),
    );
    for id in [ENGRAVABLE_BUFF, ENGRAVING_BUFF] {
        data.systems.skills.add_buff(BuffTemplate {
            id,
            max_stack: 1,
            ..Default::default()
        });
    }
    let server = test_server_with(data);
    let client = TestClient::new(&server, 1, 1001);
    let mut character = test_character(1, "Attacker", 100.0, 100.0);
    character.unit.hp = 500;
    character.unit.max_hp = 500;
    character.unit.expedition_id = 20;
    character.is_online = true;
    let player = spawn_player(&server, &client, character, 1001);
    let member = member_from_character(&server, 20, &player, false);
    server.expeditions.add(Expedition {
        faction: SystemFaction {
            id: 20,
            owner_id: 2,
            ..Default::default()
        },
        members: vec![member],
        policies: Vec::new(),
        removed_members: Vec::new(),
        is_disbanded: false,
    });
    let mut h = House::new(61, 139);
    h.current_step = -1;
    h.unit.hp = 0;
    h.unit.max_hp = 1000;
    h.unit.transform.zone_id = TEST_ZONE;
    h.unit
        .transform
        .set_xyz(105.0, 100.0, player.data().unit.transform.z);
    let house = Entity::new(2001, h);
    server.systems.housing.manager.insert(&house);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&house, &server.view_ctx());
    let target = WorldObject::from(&house);
    crate::systems::skills::buffs::add_buff_by_id(&server, &target, ENGRAVABLE_BUFF, &target, 0);
    let now = chrono::Utc::now().timestamp();
    let (_, identity) = tower_identity(&server, &house).unwrap();
    let manager = &server.systems.housing.dominions;
    manager
        .declare_unowned_with(25, identity, 10, now - 1000, || true)
        .unwrap();
    manager
        .schedule_siege(25, 20, now - 500, now + 1000, now - 600)
        .unwrap();
    manager
        .register_participant(25, 1, 20, SiegeSide::Attacker, now - 500)
        .unwrap();
    manager.mark_tower_destroyed(25, identity, now).unwrap();
    let handle = manager
        .begin_engraving(
            25,
            identity,
            1,
            20,
            crate::world::now_ms() - start_offset_ms,
        )
        .unwrap();
    let skill = SkillRun::new(
        server
            .game_data
            .systems
            .skills
            .skill(ENGRAVING_SKILL)
            .unwrap()
            .clone(),
        1,
    );
    skill.state().tl_id = 99;
    let caster = WorldObject::from(&player);
    let plot = PlotState::new(
        skill.clone(),
        CastCtx {
            caster: caster.clone(),
            caster_caster: SkillCaster::Unit(player.obj_id),
            target: Target::Obj(target),
            target_caster: SkillCastTarget::Unit(house.obj_id),
            skill_object: SkillObject::None,
        },
    );
    skill.state().active_plot = Some(plot.clone());
    player.data().unit.skill_state.active_plot = Some(PlotStateRef(plot.clone()));
    let origin = server.world.world(0).unwrap().world_pos_rot(&caster);
    server
        .systems
        .housing
        .engraving_runtime
        .active
        .lock()
        .unwrap()
        .insert(
            1,
            ActiveEngraving {
                handle: handle.clone(),
                skill: skill.clone(),
                generation: 0,
                caster,
                tower: house.clone(),
                origin,
            },
        );
    Fixture {
        server,
        player,
        house,
        skill,
        plot,
        handle,
    }
}

fn complete(f: &Fixture, skill: SkillRef) {
    let object = SkillObject::None;
    complete_effect(
        &f.server,
        &EffectArgs {
            caster: Some(WorldObject::from(&f.player)),
            caster_caster: SkillCaster::Unit(f.player.obj_id),
            target: Target::Obj(WorldObject::from(&f.house)),
            target_caster: SkillCastTarget::Unit(f.house.obj_id),
            cast: CastAction::Plot {
                plot_id: ENGRAVING_PLOT,
                event_id: 5552,
                skill_id: ENGRAVING_SKILL,
                tl_id: 99,
            },
            source: EffectSource::from_skill(skill),
            skill_object: &object,
        },
    );
}

#[tokio::test]
async fn engraving_capture_uses_original_plot_and_changes_only_territory_authority() {
    let f = fixture(300_001);
    complete(&f, f.skill.clone());
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .owner_expedition_id,
        20
    );
    assert_eq!(f.house.data().unit.owner_id, 0);
    assert_eq!(f.house.data().unit.expedition_id, 20);
    assert_eq!(f.house.data().unit.hp, 1000);
    assert!(f
        .server
        .world
        .world(0)
        .unwrap()
        .house(f.house.obj_id)
        .is_some());
    assert!(!f.house.data().unit.buffs.check_buff(ENGRAVABLE_BUFF));
    assert!(f
        .server
        .systems
        .housing
        .engraving_runtime
        .active
        .lock()
        .unwrap()
        .is_empty());
}

#[tokio::test]
async fn early_forged_cancelled_moved_dead_or_departed_guild_casters_cannot_capture() {
    let early = fixture(1);
    complete(&early, early.skill.clone());
    assert_eq!(
        early
            .server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .owner_expedition_id,
        10
    );
    let forged = fixture(300_001);
    let other = SkillRun::new(forged.skill.template.clone(), 1);
    other.state().tl_id = 99;
    complete(&forged, other);
    assert_eq!(
        forged
            .server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .owner_expedition_id,
        10
    );
    for variant in 0..5 {
        let f = fixture(300_001);
        match variant {
            0 => f.plot.request_cancellation(),
            1 => f.player.data().unit.transform.x += 1.0,
            2 => f.player.data().unit.hp = 0,
            3 => {
                f.server.expeditions.with_mut(20, |e| e.members.clear());
            }
            _ => f.player.data().is_online = false,
        }
        complete(&f, f.skill.clone());
        assert_eq!(
            f.server
                .systems
                .housing
                .dominions
                .get(25)
                .unwrap()
                .owner_expedition_id,
            10
        );
    }
}

#[tokio::test]
async fn cancellation_releases_tower_reservation_and_destruction_keeps_world_identity() {
    let f = fixture(300_001);
    abort_character(&f.server, 1);
    assert!(f.plot.cancellation_requested());
    assert!(f
        .server
        .systems
        .housing
        .dominions
        .complete_engraving(&f.handle, 20, crate::world::now_ms())
        .is_err());
    assert!(handle_tower_death(&f.server, &WorldObject::from(&f.house)));
    assert!(f
        .server
        .world
        .world(0)
        .unwrap()
        .house(f.house.obj_id)
        .is_some());
    assert!(f.server.systems.housing.manager.by_id(61).is_some());
    assert_eq!(f.house.data().unit.hp, 0);
    assert!(f.house.data().unit.buffs.check_buff(ENGRAVABLE_BUFF));
}

#[tokio::test]
async fn active_siege_and_territory_ownership_prevent_guild_disband() {
    let f = fixture(300_001);
    f.server
        .expeditions
        .with_mut(20, |e| e.faction.owner_id = f.player.character_id);
    assert!(!crate::systems::social::expedition::disband(&f.server, &f.player).await);
    assert!(!f.server.expeditions.get(20).unwrap().is_disbanded);
    complete(&f, f.skill.clone());
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .owner_expedition_id,
        20
    );
    assert!(!crate::systems::social::expedition::disband(&f.server, &f.player).await);
    assert_eq!(
        f.server.expeditions.get(20).unwrap().faction.owner_id,
        f.player.character_id
    );
}

#[tokio::test]
async fn stale_plot_cleanup_cannot_remove_the_same_characters_new_engraving_buff() {
    let f = fixture(300_001);
    let replacement = SkillRun::new(f.skill.template.clone(), 1);
    let actor = WorldObject::from(&f.player);
    let tower = WorldObject::from(&f.house);
    let template = f
        .server
        .game_data
        .systems
        .skills
        .buff(ENGRAVING_BUFF)
        .unwrap();
    let buff = crate::systems::skills::buffs::new_buff(
        &f.server,
        template,
        Some(&actor),
        &SkillCaster::Unit(f.player.obj_id),
        Some(&replacement),
        crate::world::now_ms(),
    );
    crate::systems::skills::buffs::add_buff(&f.server, &tower, buff, 0, 0);
    f.server
        .systems
        .housing
        .engraving_runtime
        .active
        .lock()
        .unwrap()
        .get_mut(&f.player.character_id)
        .unwrap()
        .skill = replacement.clone();
    assert!(cleanup_plot_buff(
        &f.server,
        &f.plot,
        &tower,
        ENGRAVING_BUFF
    ));
    assert!(f.house.data().unit.buffs.check_buff(ENGRAVING_BUFF));
    let current = PlotState::new(replacement, f.plot.ctx.clone());
    assert!(cleanup_plot_buff(
        &f.server,
        &current,
        &tower,
        ENGRAVING_BUFF
    ));
    assert!(!f.house.data().unit.buffs.check_buff(ENGRAVING_BUFF));
}

#[tokio::test]
async fn replaced_tower_damage_and_death_cannot_mutate_retained_authority() {
    let f = fixture(300_001);
    let target = WorldObject::from(&f.house);
    let attacker = WorldObject::from(&f.player);
    let before = f.server.systems.housing.dominions.get(25).unwrap();
    let world = f.server.world.world(0).unwrap();
    world.remove_object(f.house.obj_id);
    let replacement = Entity::new(f.house.obj_id, f.house.data().clone());
    world.add_object(&replacement);
    assert_eq!(
        can_attack_tower(
            &f.server,
            &Target::Obj(attacker.clone()),
            &Target::Obj(target.clone())
        ),
        Some(false)
    );
    on_hp_changed(&f.server, &target, Some(&attacker), 500, 400);
    assert!(handle_tower_death(&f.server, &target));
    assert_eq!(f.server.systems.housing.dominions.get(25).unwrap(), before);
    assert!(world
        .house(replacement.obj_id)
        .is_some_and(|live| Arc::ptr_eq(&live, &replacement)));
}

#[tokio::test]
async fn zero_hp_notification_is_restart_safe_before_the_death_callback() {
    let f = fixture(300_001);
    let manager = &f.server.systems.housing.dominions;
    let (_, identity) = tower_identity(&f.server, &f.house).unwrap();
    manager.reset_tower_destroyed(25, identity).unwrap();
    on_hp_changed(&f.server, &WorldObject::from(&f.house), None, 1000, 0);
    let state = manager.get(25).unwrap();
    assert_eq!(state.tower_hp, Some(0));
    assert!(state.tower_destroyed);
    let restarted = super::super::state::DominionManager::default();
    restarted.restore(vec![state]).unwrap();
    assert!(restarted.get(25).unwrap().tower_destroyed);
    assert!(handle_tower_death(&f.server, &WorldObject::from(&f.house)));
    assert!(f.house.data().unit.buffs.check_buff(ENGRAVABLE_BUFF));
}
