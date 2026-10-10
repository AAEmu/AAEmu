//! Byte-exact `SCUnitStatePacket` bodies for the non-character `BaseUnitType`s. The expected
//! bytes are spelled out field by field from the 1.2 C# `SCUnitStatePacket.Write`.

use aaemu_commons::{Marshal, PacketStream};

use super::unit_state::*;
use crate::data::skills::{BuffKind, BuffTemplate};
use crate::models::buff::{Buff, SkillCaster};
use crate::models::house::House;
use crate::models::item::{Item, ItemRow};
use crate::models::mate::Mate;
use crate::models::npc::Npc;
use crate::models::shipyard::{Shipyard, ShipyardData};
use crate::models::slave::Slave;
use crate::models::transfer::Transfer;
use crate::models::unit::UnitBase;
use crate::models::unit_model::{UnitCustomModelParams, UnitCustomModelType};

fn item(class: &str, id: u64, template_id: u32, grade: u8) -> Item {
    let mut i = Item::from_row(
        ItemRow {
            type_name: class.into(),
            id,
            template_id,
            ..ItemRow::default()
        },
        None,
        1,
    );
    i.grade = grade;
    i
}

fn base(name: &str, template_id: u32) -> UnitBase {
    let mut u = UnitBase {
        template_id,
        name: name.into(),
        level: 5,
        model_id: 77,
        hp: 100,
        mp: 50,
        faction_id: 13,
        scale: 1.5,
        ..UnitBase::default()
    };
    u.transform
        .set_position(10.0, 20.0, 30.0, 0.0, 0.0, std::f32::consts::PI);
    u
}

/// Everything after the kind header and owner name up to (excluding) the equipment.
fn pos_scale_level_model(s: &mut PacketStream) {
    s.write_position(10.0, 20.0, 30.0)
        .write_f32(1.5)
        .write_u8(5)
        .write_u32(77);
}

/// From the precise points to the end (no buffs), for the given attach / posture bytes.
fn tail(s: &mut PacketStream, attach: &[u8], posture: &[u8], race_gender: u8) {
    s.write_bc(0).write_i32(10_000).write_i32(5_000);
    s.write_raw(attach);
    s.write_raw(posture);
    s.write_u8(0) // ActiveWeapon
        .write_u8(0) // learnedSkillCount
        .write_i32(0); // learnedBuffCount
    s.write_i8(0).write_i8(0).write_i8(64); // yaw = pi
    s.write_u8(race_gender);
    s.write_pisc(&[0, 0, 0, 0])
        .write_pisc(&[13, 0, 0, 0])
        .write_pisc(&[0, 0])
        .write_u16(0x2000);
    s.write_u8(0).write_u8(0).write_u8(0); // good / bad / hidden buffs
}

#[test]
fn npc_unit_state_bytes() {
    let mut npc = Npc::new(1234);
    npc.unit = base("Wolf", 1234);
    npc.template_gender = 1;
    npc.template_race = 2;
    let p = npc_unit_state(0x010203, &mut npc, "", 0);

    let mut e = PacketStream::new();
    e.write_bc(0x010203).write_str("Wolf").write_u8(1); // BaseUnitType.Npc
    e.write_bc(0x010203)
        .write_u32(1234)
        .write_u32(0)
        .write_u8(0);
    e.write_str(""); // no owner
    pos_scale_level_model(&mut e);
    for _ in 0..28 {
        e.write_i32(0);
    }
    // No equipment at all: ModelParams.SetType(Skin) -> type 2 + hair, skin, model ids.
    e.write_u8(2).write_u32(0).write_u32(0).write_u32(0);
    tail(&mut e, &[255, 0xFF], &[0, 0], 0x12);
    assert_eq!(p.body, e.as_bytes());
    assert_eq!(npc.unit.model_params.kind, UnitCustomModelType::SKIN);
}

#[test]
fn npc_equipment_posture_and_buffs() {
    let mut npc = Npc::new(1234);
    npc.unit = base("Wolf", 1234);
    npc.unit.owner_id = 9;
    npc.anim_action_id = 0x55;
    npc.equipment[0] = Some(item("Item", 0x1122334455, 500, 3));
    npc.equipment[19] = Some(item("BodyPart", 6, 600, 0));
    npc.equipment[27] = Some(item("BodyPart", 7, 700, 0));
    npc.unit.model_params = UnitCustomModelParams::default();
    let mut t = BuffTemplate::simple(42, BuffKind::Bad);
    t.duration = 1000;
    let mut buff = Buff::new(t, SkillCaster::default(), None, 100);
    buff.duration = 1000;
    npc.unit.buffs.add_buff(buff, 0, 0, 100);
    let p = npc_unit_state(0x10, &mut npc, "Owner", 400);

    let mut e = PacketStream::new();
    e.write_bc(0x10).write_str("Wolf").write_u8(1);
    e.write_bc(0x10).write_u32(1234).write_u32(9).write_u8(0);
    e.write_str("Owner");
    pos_scale_level_model(&mut e);
    e.write_u32(500).write_u64(0x1122334455).write_u8(3); // slot 0: templateId, id, grade
    for _ in 1..19 {
        e.write_i32(0);
    }
    e.write_u32(600); // body part 19
    for _ in 20..=26 {
        e.write_i32(0);
    }
    e.write_u32(700); // cosplay: BodyPart.Write = template id only
    e.write_u8(0); // model params untouched (has equipment)
    let mut rest = PacketStream::new();
    tail(&mut rest, &[255, 0xFF], &[4, 0, 0x55, 0, 0, 0, 1], 0);
    let rest = rest.into_bytes();
    // Replace the empty buff lists with one bad buff.
    e.write_raw(&rest[..rest.len() - 3]);
    e.write_u8(0).write_u8(1);
    let b = &npc.unit.buffs.effects[0];
    b.write_net(&mut e, 400);
    e.write_u8(0);
    assert_eq!(p.body, e.as_bytes());
}

#[test]
fn slave_unit_state_bytes() {
    let mut sl = Slave::new(321, 55);
    sl.unit = base("Boat", 55);
    sl.unit.tl_id = 7;
    sl.summoner_id = 99;
    sl.attach_point_id = 3;
    sl.owner_obj_id = 0x0A0B0C;
    sl.bonding_obj_id = 0x000102;
    let p = slave_unit_state(0x20, &mut sl, "", 0);

    let mut e = PacketStream::new();
    e.write_bc(0x20).write_str("Boat").write_u8(2);
    e.write_u32(321).write_u16(7).write_u32(55).write_u32(99);
    e.write_str("");
    pos_scale_level_model(&mut e);
    for _ in 0..28 {
        e.write_i32(0);
    }
    e.write_u8(0); // model params (slaves never get SetType(Skin))
                   // AttachPoint1: point 3 + OwnerObjId bc; AttachPoint2: BondingObjId bc.
                   // TurretState posture: type 8, isLooted, pitch, yaw.
    let mut posture = vec![8, 0];
    posture.extend_from_slice(&[0; 8]);
    tail(
        &mut e,
        &[3, 0x0C, 0x0B, 0x0A, 0x02, 0x01, 0x00],
        &posture,
        0,
    );
    assert_eq!(p.body, e.as_bytes());

    // Unattached: AttachPointId -1 writes no owner, no bonding -> -1.
    sl.attach_point_id = -1;
    sl.bonding_obj_id = 0;
    let p2 = slave_unit_state(0x20, &mut sl, "", 0);
    assert_eq!(p2.body.len(), p.body.len() - 5);
}

#[test]
fn house_unit_state_bytes() {
    let mut h = House::new(1, 300);
    h.unit = base("Home", 300);
    h.unit.tl_id = 4;
    h.build_step_count = 5;
    h.current_step = 2;
    let p = house_unit_state(0x30, &mut h, "", 0);

    let mut e = PacketStream::new();
    e.write_bc(0x30).write_str("Home").write_u8(3);
    e.write_u16(4).write_u32(300).write_i16(-3); // -BuildSteps.Count + CurrentStep
    e.write_str("");
    pos_scale_level_model(&mut e);
    for _ in 0..28 {
        e.write_i32(0);
    }
    e.write_u8(0);
    // HouseState: type 1, isLooted, 2 doors + 6 windows.
    tail(&mut e, &[255, 0xFF], &[1, 0, 1, 1, 1, 1, 1, 1, 1, 1], 0);
    assert_eq!(p.body, e.as_bytes());

    h.current_step = -1;
    let p = house_unit_state(0x30, &mut h, "", 0);
    assert_eq!(p.body[4 + 2 + 4 + 1 + 2 + 4..][..2], [0, 0]);
}

#[test]
fn transfer_and_shipyard_write_only_body_parts() {
    let mut t = Transfer::new(66);
    t.unit = base("Cart", 66);
    t.unit.tl_id = 2;
    t.attach_point_id = 1; // Driver: not System -> BondingObjId follows
    t.bonding_obj_id = 0x11;
    let p = transfer_unit_state(0x40, &mut t, "", 0);
    let mut e = PacketStream::new();
    e.write_bc(0x40).write_str("Cart").write_u8(4);
    e.write_u16(2).write_u32(66);
    e.write_str("");
    pos_scale_level_model(&mut e);
    for _ in 0..7 {
        e.write_i32(0);
    }
    e.write_u8(0);
    let mut posture = vec![8, 0];
    posture.extend_from_slice(&[0; 8]);
    tail(&mut e, &[1, 0x11, 0, 0, 0xFF], &posture, 0);
    assert_eq!(p.body, e.as_bytes());

    let mut sy = Shipyard::new(ShipyardData {
        id: 0x0102030405060708,
        template_id: 9,
        ..ShipyardData::default()
    });
    sy.unit = base("Yard", 9);
    let p = shipyard_unit_state(0x50, &mut sy, "", 0);
    let mut e = PacketStream::new();
    e.write_bc(0x50).write_str("Yard").write_u8(6);
    e.write_u64(0x0102030405060708).write_u32(9);
    e.write_str("");
    pos_scale_level_model(&mut e);
    for _ in 0..7 {
        e.write_i32(0);
    }
    e.write_u8(0);
    tail(&mut e, &[255, 0xFF], &[0, 0], 0);
    assert_eq!(p.body, e.as_bytes());
}

#[test]
fn mate_unit_state_bytes() {
    let mut m = Mate::new(5, 88);
    m.unit = base("Horse", 88);
    m.unit.tl_id = 3;
    m.unit.owner_id = 1000;
    m.equipment[0] = Some(item("Item", 1, 900, 2));
    let p = mate_unit_state(0x60, &mut m, "Rider", 0);
    let mut e = PacketStream::new();
    e.write_bc(0x60).write_str("Horse").write_u8(5);
    e.write_u16(3).write_u32(88).write_u32(1000);
    e.write_str("Rider");
    pos_scale_level_model(&mut e);
    m.equipment[0].as_ref().unwrap().write(&mut e); // full Item.Write
    for _ in 1..28 {
        e.write_i32(0);
    }
    e.write_u8(0);
    tail(&mut e, &[255, 0xFF], &[0, 0], 0);
    assert_eq!(p.body, e.as_bytes());
}
