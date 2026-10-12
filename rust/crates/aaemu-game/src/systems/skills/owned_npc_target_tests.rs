//! The 1.2 rod reactions may only target NPCs spawned for their own character.

use std::sync::Arc;

use crate::data::GameData;
use crate::models::npc::Npc;
use crate::test_support::*;
use crate::world::{Entity, NpcRef, PlayerRef, WorldObject};

use crate::systems::skills::data::effects::{EffectTemplate, HealEffect};
use crate::systems::skills::data::{BuffTemplate, EffectKind};
use crate::systems::skills::state::{CancelToken, SkillTaskKind, SkillTaskSlot};
use crate::systems::skills::types::{skill_result, SkillCastTarget, SkillCaster};

const SKILL: u32 = 21196;
const BUFF: u32 = 50002;
const CHARACTER: u32 = 7;
const PLAYER: u32 = 0x100;
const NPC: u32 = 0x200;

fn data(restricted: bool) -> GameData {
    let mut data = GameData::empty();
    data.formulas = crate::systems::vehicles::stats::tests::formulas(
        &[(1, "1000", 14, 5), (2, "1000", 15, 5)],
        &[],
    );
    data.systems.skills.add_effect(
        EffectKind::HealEffect,
        EffectTemplate::Heal(HealEffect {
            id: 1,
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
        900001,
    );
    let mut skill = crate::systems::skills::tests::skill_template(SKILL);
    skill.target_my_npc = restricted;
    skill.mana_cost = 25;
    skill.consume_labor_power = 100;
    skill.cancel_ongoing_buffs = true;
    skill.cooldown_time = 10000;
    skill
        .effects
        .push(crate::systems::skills::tests::skill_effect(
            900001,
            EffectKind::HealEffect,
            1,
        ));
    data.systems.skills.add_skill(skill);
    data.systems.skills.add_buff(BuffTemplate {
        id: BUFF,
        kind: 1,
        duration: 0,
        stack_rule: 1,
        remove_on_start_skill: true,
        ..Default::default()
    });
    data
}

fn actors(
    server: &crate::server::GameServer,
    client: &TestClient,
    owner: u32,
) -> (PlayerRef, NpcRef) {
    let mut character = test_character(CHARACTER, "Fisherman", 100.0, 100.0);
    character.unit.hp = 500;
    character.unit.max_hp = 1000;
    character.unit.mp = 100;
    character.unit.max_mp = 1000;
    character.labor_power = 500;
    let player = spawn_player(server, client, character, PLAYER);
    let mut fish = Npc::new(12835);
    fish.unit.hp = 500;
    fish.unit.max_hp = 1000;
    fish.unit.owner_id = owner;
    fish.unit.transform.set_xyz(103.0, 100.0, 100.0);
    let npc = Entity::new(NPC, fish);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&npc, &server.view_ctx());
    (player, npc)
}

#[tokio::test]
async fn flagged_skill_accepts_character_id_owner_and_applies_the_cast() {
    let server = Arc::new(test_server_with(data(true)));
    crate::systems::skills::bind(&server);
    let mut client = TestClient::new(&server, 1, CHARACTER);
    let (player, npc) = actors(&server, &client, CHARACTER);
    let caster = WorldObject::from(&player);
    crate::systems::skills::buffs::add_buff_by_id(&server, &caster, BUFF, &caster, 0);
    let skill = super::new_skill_for(
        &server,
        server
            .game_data
            .systems
            .skills
            .skill(SKILL)
            .unwrap()
            .clone(),
        Some(&caster),
    );
    let result = super::use_skill(
        &server,
        &skill,
        &caster,
        SkillCaster::Unit(PLAYER),
        &mut SkillCastTarget::Unit(NPC),
        None,
        false,
    );
    assert_eq!(result.0, skill_result::SUCCESS);
    assert_eq!(npc.data().unit.hp, 650);
    let p = player.data();
    assert_eq!(p.unit.mp, 75);
    assert!(p.unit.skill_last_used_ms > 0);
    assert!(!p.unit.buffs.check_buff(BUFF));
    drop(p);
    assert!(!client.drain().is_empty());
}

#[tokio::test]
async fn flagged_skill_rejects_invalid_targets_before_resources_buffs_gcd_or_cast_mutation() {
    for (owner, mut target) in [
        (CHARACTER + 1, SkillCastTarget::Unit(NPC)),
        // An object id that happens to match the caster must not count as ownership.
        (PLAYER, SkillCastTarget::Unit(NPC)),
        (0, SkillCastTarget::Unit(NPC)),
        (CHARACTER, SkillCastTarget::Unit(PLAYER + 1)),
        (CHARACTER, SkillCastTarget::Unit(PLAYER)),
        (CHARACTER, SkillCastTarget::Unit(0)),
        (
            CHARACTER,
            SkillCastTarget::Position {
                obj_id: 0,
                x: 103.0,
                y: 100.0,
                z: 100.0,
                rot: 0.0,
                obj_id1: 0,
                obj_id2: 0,
            },
        ),
    ] {
        let server = Arc::new(test_server_with(data(true)));
        crate::systems::skills::bind(&server);
        let mut client = TestClient::new(&server, 1, CHARACTER);
        let (player, npc) = actors(&server, &client, owner);
        let foreign_client = TestClient::new(&server, 2, CHARACTER + 1);
        spawn_player(
            &server,
            &foreign_client,
            test_character(CHARACTER + 1, "Other", 102.0, 100.0),
            PLAYER + 1,
        );
        let caster = WorldObject::from(&player);
        crate::systems::skills::buffs::add_buff_by_id(&server, &caster, BUFF, &caster, 0);
        let existing = super::new_skill_for(
            &server,
            server
                .game_data
                .systems
                .skills
                .skill(SKILL)
                .unwrap()
                .clone(),
            Some(&caster),
        );
        let old_task = SkillTaskSlot {
            kind: SkillTaskKind::Cast,
            skill: existing,
            cancel: CancelToken::new(),
        };
        {
            let mut p = player.data();
            p.unit.skill_state.skill_task = Some(old_task.clone());
            p.unit.skill_state.condition_chance = false;
            p.skill_char.skill_cancelled = true;
        }
        client.drain();
        let skill = super::new_skill_for(
            &server,
            server
                .game_data
                .systems
                .skills
                .skill(SKILL)
                .unwrap()
                .clone(),
            Some(&caster),
        );
        assert_eq!(
            super::use_skill(
                &server,
                &skill,
                &caster,
                SkillCaster::Unit(PLAYER),
                &mut target,
                None,
                false,
            ),
            (56, 0) // r208022 skill_not_my_npc
        );
        let p = player.data();
        assert_eq!((p.unit.hp, p.unit.mp, p.labor_power), (500, 100, 500));
        assert_eq!(
            (p.unit.skill_last_used_ms, p.unit.global_cooldown_ms),
            (0, 0)
        );
        assert!(p.unit.buffs.check_buff(BUFF));
        assert!(p.skill_char.skill_cancelled);
        assert!(!p.unit.skill_state.condition_chance);
        assert_eq!(p.unit.skill_state.skill_task.as_ref(), Some(&old_task));
        assert!(p.unit.skill_state.active_plot.is_none());
        drop(p);
        assert!(!old_task.cancel.is_cancelled());
        assert_eq!(skill.tl_id(), 0);
        assert_eq!(skill.state().initial_target, 0);
        assert_eq!(npc.data().unit.hp, 500);
        assert!(client.drain().is_empty());
    }
}

#[tokio::test]
async fn unflagged_skill_keeps_foreign_npc_targeting() {
    let server = Arc::new(test_server_with(data(false)));
    crate::systems::skills::bind(&server);
    let client = TestClient::new(&server, 1, CHARACTER);
    let (player, npc) = actors(&server, &client, CHARACTER + 1);
    let caster = WorldObject::from(&player);
    let skill = super::new_skill_for(
        &server,
        server
            .game_data
            .systems
            .skills
            .skill(SKILL)
            .unwrap()
            .clone(),
        Some(&caster),
    );
    assert_eq!(
        super::use_skill(
            &server,
            &skill,
            &caster,
            SkillCaster::Unit(PLAYER),
            &mut SkillCastTarget::Unit(NPC),
            None,
            false,
        )
        .0,
        skill_result::SUCCESS
    );
    assert_eq!(npc.data().unit.hp, 650);
    assert_eq!(player.data().unit.mp, 75);
}
