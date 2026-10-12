//! Client buff cancellation may only change the player's own companions and vehicles.

use std::sync::Arc;

use aaemu_commons::PacketStream;

use crate::data::GameData;
use crate::models::slave::Slave;
use crate::net::registry::ClientPacket;
use crate::test_support::*;
use crate::world::{Entity, WorldObject};

use super::data::BuffTemplate;
use super::handlers::CSRemoveBuffPacket;

fn mate_cast_data() -> GameData {
    use super::data::effects::{EffectTemplate, HealEffect};
    use super::data::EffectKind;
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
    let mut skill = super::tests::skill_template(20001);
    skill.target_type = 0;
    skill.damage_type_id = 0;
    skill.mana_cost = 25;
    skill.cancel_ongoing_buffs = true;
    skill.effects.push(super::tests::skill_effect(
        900001,
        EffectKind::HealEffect,
        1,
    ));
    data.systems.skills.add_skill(skill);
    data.systems.skills.add_buff(BuffTemplate {
        id: 50002,
        kind: 1,
        duration: 0,
        stack_rule: 1,
        remove_on_start_skill: true,
        ..Default::default()
    });
    // No learned/static mount skill relation: actor authority must not silently
    // prohibit existing temporary skill grants or impose an unproven allowlist.
    data
}

fn mate_cast_packet(obj_id: u32) -> super::handlers::CSStartSkillPacket {
    let mut body = PacketStream::new();
    body.write_u32(20001)
        .write_u8(super::types::caster_type::MOUNT)
        .write_bc(obj_id)
        .write_u32(77)
        .write_u8(0)
        .write_bc(obj_id)
        .write_u8(0);
    let mut body = PacketStream::from_bytes(body.into_bytes());
    super::handlers::CSStartSkillPacket::read(&mut body).unwrap()
}

#[tokio::test]
async fn mate_skill_packet_rejects_foreign_actor_before_costs_and_allows_unmounted_owner() {
    use crate::models::mate::Mate;
    let server = Arc::new(test_server_with(mate_cast_data()));
    super::bind(&server);
    let mut foreign = TestClient::new(&server, 1, 1);
    let mut owner = TestClient::new(&server, 2, 2);
    let a = spawn_player(
        &server,
        &foreign,
        test_character(1, "Foreign", 100.0, 100.0),
        0x100,
    );
    let b = spawn_player(
        &server,
        &owner,
        test_character(2, "Owner", 102.0, 100.0),
        0x101,
    );
    foreign.conn.active_char = Some(a);
    owner.conn.active_char = Some(b.clone());
    let mut data = Mate::new(1, 1);
    data.owner_obj_id = b.obj_id;
    data.unit.hp = 500;
    data.unit.mp = 100;
    data.unit.transform.set_xyz(103.0, 100.0, 100.0);
    let mate = Entity::new(0x201, data);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&mate, &server.view_ctx());
    let caster = WorldObject::from(&mate);
    super::buffs::add_buff_by_id(&server, &caster, 50002, &caster, 0);
    mate_cast_packet(mate.obj_id)
        .handle(&server, &mut foreign.conn)
        .await
        .unwrap();
    {
        let data = mate.data();
        assert_eq!(
            (data.unit.hp, data.unit.mp, data.unit.skill_last_used_ms),
            (500, 100, 0)
        );
        assert!(data.unit.buffs.check_buff(50002));
    }
    assert!(!b.data().is_riding);
    mate_cast_packet(mate.obj_id)
        .handle(&server, &mut owner.conn)
        .await
        .unwrap();
    let data = mate.data();
    assert_eq!((data.unit.hp, data.unit.mp), (650, 75));
    assert!(data.unit.skill_last_used_ms > 0);
    assert!(!data.unit.buffs.check_buff(50002));
}

#[tokio::test]
async fn mate_skill_packet_requires_real_passenger_attachment_and_keeps_unlisted_skills() {
    use crate::data::items::{ItemTemplate, TemplateKind};
    use crate::models::container::slot_type;
    use crate::models::item::Item;
    use crate::systems::npcs::npc::data::NpcTemplate;
    use crate::systems::vehicles::{data::attach_point, mates};
    let mut data = mate_cast_data();
    data.systems.npcs.npc.templates.insert(
        900,
        NpcTemplate {
            id: 900,
            name: "Horse".into(),
            level: 1,
            scale: 1.0,
            ..Default::default()
        },
    );
    data.items.templates.insert(
        5000,
        ItemTemplate {
            kind: TemplateKind::SummonMate { npc_id: 900 },
            ..ItemTemplate::plain(5000, 1)
        },
    );
    let server = Arc::new(test_server_with(data));
    server.set_self();
    super::bind(&server);
    let mut client = TestClient::new(&server, 1, 1);
    let owner_client = TestClient::new(&server, 2, 2);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Passenger", 100.0, 100.0),
        0x00F0_0001,
    );
    let owner = spawn_player(
        &server,
        &owner_client,
        test_character(2, "Owner", 102.0, 100.0),
        0x00F0_0002,
    );
    client.conn.active_char = Some(player.clone());
    let template = server.game_data.items.template(5000).unwrap();
    let mut item = Item::create(0x7002, template, 1, 0, 1, &server.game_data.items, 0);
    item.slot_type = slot_type::INVENTORY;
    item.slot = 0;
    let bag = owner
        .data()
        .inventory
        .get(slot_type::INVENTORY)
        .cloned()
        .unwrap();
    bag.lock().unwrap().items.push(item);
    mates::spawn_mount(&server, &owner, 0x7002);
    let mate = server
        .systems
        .vehicles
        .mates
        .get(0)
        .get_active_mates(2)
        .pop()
        .unwrap();
    let tl_id = {
        let mut data = mate.data();
        data.unit.hp = 500;
        data.unit.mp = 100;
        data.passenger_mut(attach_point::PASSENGER0).unwrap().obj_id = player.obj_id;
        u32::from(data.unit.tl_id)
    };
    // A stale registry entry without the actual riding relation is insufficient.
    {
        let mut p = player.data();
        p.attached_point = attach_point::PASSENGER0;
        p.is_riding = true;
    }
    mate_cast_packet(mate.obj_id)
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    {
        let data = mate.data();
        assert_eq!((data.unit.hp, data.unit.mp), (500, 100));
    }
    // Establish the normal server-managed passenger seat and parent relation.
    mate.data()
        .passenger_mut(attach_point::PASSENGER0)
        .unwrap()
        .obj_id = 0;
    mates::mount_mate(&server, &player, tl_id, attach_point::PASSENGER0, 0);
    assert_eq!(player.data().unit.transform.parent_obj_id, mate.obj_id);
    assert!(mate.data().skills.is_empty());
    mate_cast_packet(mate.obj_id)
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    let data = mate.data();
    assert_eq!((data.unit.hp, data.unit.mp), (650, 75));
}

#[tokio::test]
async fn remove_buff_packet_preserves_foreign_slave_buff_and_removes_owned_slave_buff() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        id: 50001,
        kind: 1,
        duration: 0,
        stack_rule: 1,
        ..Default::default()
    });
    let server = Arc::new(test_server_with(data));
    super::bind(&server);
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Owner", 100.0, 100.0),
        0x100,
    );
    client.conn.active_char = Some(player.clone());
    let world = server.world.world(0).expect("world");
    let caster = WorldObject::from(&player);
    for (obj_id, summoner_id, removable) in [(0x200, 2, false), (0x201, 1, true)] {
        let mut data = Slave::new(obj_id, 1);
        data.summoner_id = summoner_id;
        data.unit.transform.set_xyz(103.0, 100.0, 100.0);
        let slave = Entity::new(obj_id, data);
        world.spawn(&slave, &server.view_ctx());
        let owner = WorldObject::from(&slave);
        super::buffs::add_buff_by_id(&server, &owner, 50001, &caster, 0);
        let index = slave.data().unit.buffs.effects[0].index;
        let mut body = PacketStream::new();
        body.write_bc(obj_id).write_u32(index).write_u8(0);
        let packet = CSRemoveBuffPacket::read(&mut body).expect("remove buff body");
        packet
            .handle(&server, &mut client.conn)
            .await
            .expect("remove buff");
        assert_eq!(slave.data().unit.buffs.check_buff(50001), !removable);
    }
}

#[test]
fn start_skill_rejects_unknown_caster_type() {
    // Unknown wire discriminants already fail before the skill-use permission branches.
    let mut body = PacketStream::from_bytes(vec![1, 0, 0, 0, 255]);
    assert!(super::handlers::CSStartSkillPacket::read(&mut body).is_err());
}
