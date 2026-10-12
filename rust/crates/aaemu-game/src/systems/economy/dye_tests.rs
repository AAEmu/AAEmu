//! Dye validation and deferred source consumption through the skill effect pipeline.

use std::sync::Arc;

use crate::data::items::ItemTemplate;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::tests::{give, item_at, test_data, World, ARMOR};
use crate::systems::skills::data::effects::SpecialEffect;
use crate::systems::skills::data::{EffectKind, EffectTemplate, SkillEffect, SkillTemplate};
use crate::systems::skills::handlers::CSStartSkillPacket;
use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

use super::*;

const DYE: u32 = 29382;
const OTHER_COLOR: u32 = 28699;
const UNDYEABLE: u32 = 2001;
const SKILL: u32 = 22727;

fn fixture() -> World {
    let mut data = test_data();
    let mut undyeable = data.items.template(ARMOR).unwrap().clone();
    undyeable.id = UNDYEABLE;
    data.items.templates.insert(UNDYEABLE, undyeable);
    data.items.default_dye_ids.insert(ARMOR, 26769);
    for id in [DYE, OTHER_COLOR] {
        let mut dye = ItemTemplate::plain(id, 20);
        dye.category_id = CATEGORY_DYE;
        dye.use_skill_id = SKILL;
        dye.use_skill_as_reagent = true;
        data.items.templates.insert(id, dye);
    }
    data.systems.skills.add_effect(
        EffectKind::SpecialEffect,
        EffectTemplate::Special(SpecialEffect {
            id: 12678,
            special_effect_type_id: 98,
            value1: 0,
            value2: 0,
            value3: 0,
            value4: 0,
        }),
        31866,
    );
    data.systems.skills.add_skill(SkillTemplate {
        id: SKILL,
        target_type: 9,
        max_range: 4,
        // Apply immediately in this fixture; real 1.2 uses a five-second cast.
        effects: vec![SkillEffect {
            effect_id: 31866,
            template: Some((EffectKind::SpecialEffect, 12678)),
            weight: 0,
            start_level: 0,
            end_level: 255,
            friendly: true,
            non_friendly: true,
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
            consume_item_count: 1,
            always_hit: true,
            item_set_id: 0,
            interaction_success_hit: false,
        }],
        ..SkillTemplate::default()
    });
    let server = Arc::new(test_server_with(data));
    server.set_self();
    crate::systems::skills::bind(&server);
    super::install(&server);
    let mut a = TestClient::new(&server, 1, 1001);
    let mut b = TestClient::new(&server, 2, 1002);
    let pa = spawn_player(&server, &a, test_character(1, "Dyer", 100.0, 100.0), 0x100);
    let pb = spawn_player(&server, &b, test_character(2, "Other", 101.0, 100.0), 0x101);
    a.conn.active_char = Some(pa.clone());
    b.conn.active_char = Some(pb.clone());
    for player in [&pa, &pb] {
        server.items.add_container(Inv::of(player).bag());
    }
    a.drain();
    b.drain();
    World {
        server,
        a,
        pa,
        b,
        pb,
    }
}

async fn cast(w: &mut World, dye: u64, target: u64) {
    CSStartSkillPacket {
        skill_id: SKILL,
        caster: SkillCaster::Item {
            obj_id: w.pa.obj_id,
            item_id: dye,
            item_template_id: DYE,
            type1: 0,
            type2: 0,
        },
        target: SkillCastTarget::Item {
            obj_id: w.pa.obj_id,
            id: target,
            type1: 0,
            type2: 0,
        },
        flag: 0,
        skill_object: SkillObject::None,
    }
    .handle(&w.server, &mut w.a.conn)
    .await
    .unwrap();
}

#[tokio::test]
async fn dye_skill_updates_allowed_owned_equipment_and_consumes_one_source() {
    let mut w = fixture();
    let dye = give(&w, &w.pa, slot_type::INVENTORY, DYE, 2, 0);
    let target = give(&w, &w.pa, slot_type::INVENTORY, ARMOR, 1, 1);
    cast(&mut w, dye, target).await;
    assert_eq!(item_at(&w.pa, slot_type::INVENTORY, 0).unwrap().count, 1);
    let after = item_at(&w.pa, slot_type::INVENTORY, 1).unwrap();
    assert_eq!(after.details.dye_item_id, DYE);
    assert!(after.dirty);
    assert!(!w.pa.data().skill_char.skill_cancelled);
    assert!(w.a.drain().iter().any(|(op, body)| {
        *op == sc::SCItemTaskSuccessPacket && body.first() == Some(&task_type::DYEING)
    }));
}

#[tokio::test]
async fn dye_skill_rejects_undyeable_and_foreign_targets_without_debit() {
    for foreign in [false, true] {
        let mut w = fixture();
        let dye = give(&w, &w.pa, slot_type::INVENTORY, DYE, 2, 0);
        let owner = if foreign { &w.pb } else { &w.pa };
        let template = if foreign { ARMOR } else { UNDYEABLE };
        let target = give(&w, owner, slot_type::INVENTORY, template, 1, 1);
        let before = item_at(owner, slot_type::INVENTORY, 1).unwrap();
        cast(&mut w, dye, target).await;
        assert_eq!(item_at(&w.pa, slot_type::INVENTORY, 0).unwrap().count, 2);
        let owner = if foreign { &w.pb } else { &w.pa };
        assert_eq!(item_at(owner, slot_type::INVENTORY, 1).unwrap(), before);
        assert!(w.pa.data().skill_char.skill_cancelled);
        assert!(!w.a.drain().iter().any(|(op, body)| {
            *op == sc::SCItemTaskSuccessPacket && body.first() == Some(&task_type::DYEING)
        }));
    }
}

#[tokio::test]
async fn dye_hook_preserves_other_colors_and_rejects_foreign_or_mismatched_source() {
    let w = fixture();
    let target = give(&w, &w.pa, slot_type::INVENTORY, ARMOR, 1, 1);
    let foreign = give(&w, &w.pb, slot_type::INVENTORY, OTHER_COLOR, 2, 0);
    EconomyForSkills.dye_item(&w.server, &w.pa, foreign, OTHER_COLOR, target);
    assert_eq!(
        item_at(&w.pa, slot_type::INVENTORY, 1)
            .unwrap()
            .details
            .dye_item_id,
        0
    );
    assert!(w.pa.data().skill_char.skill_cancelled);
    let dye = give(&w, &w.pa, slot_type::INVENTORY, OTHER_COLOR, 2, 0);
    w.pa.data().skill_char.skill_cancelled = false;
    EconomyForSkills.dye_item(&w.server, &w.pa, dye, DYE, target);
    assert_eq!(
        item_at(&w.pa, slot_type::INVENTORY, 1)
            .unwrap()
            .details
            .dye_item_id,
        0
    );
    assert!(w.pa.data().skill_char.skill_cancelled);
    w.pa.data().skill_char.skill_cancelled = false;
    EconomyForSkills.dye_item(&w.server, &w.pa, dye, OTHER_COLOR, target);
    assert_eq!(
        item_at(&w.pa, slot_type::INVENTORY, 1)
            .unwrap()
            .details
            .dye_item_id,
        OTHER_COLOR
    );
    assert!(!w.pa.data().skill_char.skill_cancelled);
}
