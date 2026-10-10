//! Body builders for the packets whose C# `Write` reads a live unit:
//! `SCCharacterStatePacket`, `SCUnitStatePacket` (every `BaseUnitType`) and
//! `SCCreateCharacterResponsePacket`.
//!
//! The character branch of `SCUnitStatePacket` is [`unit_state`]; the other kinds go through
//! [`write_net_unit`] with a [`NetUnitSpec`] describing their kind-specific fields (built by
//! [`npc_unit_state`], [`slave_unit_state`], ...).

use aaemu_commons::{Marshal, PacketStream};

use crate::data::skills::BuffKind;
use crate::data::GameData;
use crate::models::buff::{Buff, SkillCaster};
use crate::models::character::Character;
use crate::models::house::House;
use crate::models::item::Item;
use crate::models::mate::Mate;
use crate::models::npc::Npc;
use crate::models::shipyard::Shipyard;
use crate::models::slave::Slave;
use crate::models::transfer::Transfer;
use crate::models::unit::{attach_point, BaseUnitType, ModelPostureType, UnitBase, UnitData};
use crate::models::unit_model::UnitCustomModelType;
use crate::packets::world::{
    SCCharacterStatePacket, SCCreateCharacterResponsePacket, SCUnitStatePacket,
};

/// `BaseUnitType.Character`.
const BASE_UNIT_TYPE_CHARACTER: u8 = 0;
/// `AttachPointKind.System`.
const ATTACH_POINT_SYSTEM: u8 = 255;
/// `ModelPostureType.None`.
const MODEL_POSTURE_NONE: u8 = 0;
/// The patron / auction-license buffs `SCUnitStatePacket` force-adds to characters
/// ("TODO Wrong place" in C#).
pub const UNIT_STATE_FORCED_BUFFS: [u32; 2] = [8_000_011, 8_000_012];

/// `SCCharacterStatePacket(character)`.
pub fn character_state(c: &Character) -> SCCharacterStatePacket {
    let mut s = PacketStream::new();
    s.write_u32(c.unit.transform.instance_id) // instanceId
        .write_u16(16) // Guid: Write(byte[], appendSize: true)
        .write_raw(&c.guid)
        .write_i32(0); // rwd
    c.write(&mut s);
    s.write_f32(c.unit.transform.rot_x)
        .write_f32(c.unit.transform.rot_y)
        .write_f32(c.unit.transform.rot_z)
        .write_i32(c.experience)
        .write_i32(c.recoverable_exp)
        .write_u32(0) // penaltiedExp
        .write_i32(0) // returnDistrictId
        .write_u32(0) // returnDistrict
        .write_u32(c.resurrection_district_id);
    for _ in 0..11 {
        s.write_u32(0); // abilityExp (General .. Love)
    }
    s.write_i32(c.unread_mail.received)
        .write_i32(c.unread_mail.mia_received)
        .write_i32(c.unread_mail.commercial_received)
        .write_u8(c.num_inventory_slots)
        .write_i16(c.num_bank_slots)
        .write_i64(c.money)
        .write_i64(c.money2)
        .write_i64(0)
        .write_i64(0)
        .write_bool(c.auto_use_aa_point)
        .write_i32(c.jury_point)
        .write_i32(0) // jailSeconds
        .write_i64(0) // bountyMoney
        .write_i64(0) // bountyTime
        .write_i32(c.reported_as_bot_count)
        .write_i32(0) // suspectedNo
        .write_i32(c.online_time as i32) // totalPlayTime
        .write_i64(c.created)
        .write_u8(c.expanded_expert);
    SCCharacterStatePacket {
        body: s.into_bytes(),
    }
}

/// `SCCreateCharacterResponsePacket(character)`.
pub fn create_character_response(c: &Character) -> SCCreateCharacterResponsePacket {
    let mut s = PacketStream::new();
    c.write(&mut s);
    SCCreateCharacterResponsePacket {
        body: s.into_bytes(),
    }
}

/// `SCUnitStatePacket(character).Write`. As in C#, writing can add the two forced buffs to the
/// character. Exceptions inside the C# `Write` are caught by `PacketStream.Write(PacketMarshaler)`
/// and the packet is sent truncated at that point: this happens when the character has no
/// visual options / `Stp` yet, when a forced buff has no template, or for a buff of an unknown
/// kind; the same truncation is reproduced here.
pub fn unit_state(c: &mut Character, data: &GameData, now_ms: i64) -> SCUnitStatePacket {
    let mut s = PacketStream::new();
    write_unit_state(c, data, now_ms, &mut s);
    SCUnitStatePacket {
        body: s.into_bytes(),
    }
}

fn write_unit_state(c: &mut Character, data: &GameData, now_ms: i64, s: &mut PacketStream) {
    s.write_bc(c.obj_id)
        .write_str(&c.unit.name)
        .write_u8(BASE_UNIT_TYPE_CHARACTER)
        .write_u32(c.id) // type(id)
        .write_i64(0) // v?
        .write_str(""); // OwnerId == 0 -> no master name
    s.write_position(c.unit.transform.x, c.unit.transform.y, c.unit.transform.z)
        .write_f32(1.0) // Scale
        .write_u8(c.unit.level)
        .write_u32(c.unit.model_id);

    // Inventory_Equip (Character)
    let slots = c.inventory.equipment_slots();
    for (index, item) in slots.iter().enumerate().take(28) {
        let body_part = (19..=25).contains(&index);
        match item {
            Some(item) if body_part => {
                s.write_u32(item.template_id);
            }
            Some(item) => item.write(s),
            None => {
                s.write_i32(0);
            }
        }
    }
    for _ in slots.len()..28 {
        // GetItemBySlot past the container size returns null.
        s.write_i32(0);
    }

    c.unit.model_params.write(s);
    s.write_bc(0)
        .write_i32(c.unit.hp.wrapping_mul(100)) // preciseHealth
        .write_i32(c.unit.mp.wrapping_mul(100)) // preciseMana
        .write_u8(ATTACH_POINT_SYSTEM); // AttachPoint1
                                        // AttachPoint2: `Bonding` (BondDoodad.Write) or -1.
    match &c.bonding {
        Some(b) => b.write(s),
        None => {
            s.write_i8(-1);
        }
    }
    // Unit.ModelPosture
    s.write_u8(MODEL_POSTURE_NONE).write_bool(false); // isLooted
    s.write_u8(0); // ActiveWeapon

    s.write_u8(c.skills.skills.len() as u8);
    for sk in &c.skills.skills {
        s.write_u32(sk.id).write_u8(sk.level);
    }
    s.write_i32(c.skills.passive_buffs.len() as i32);
    for b in &c.skills.passive_buffs {
        s.write_u32(*b);
    }

    let (roll, pitch, yaw) = c.unit.transform.roll_pitch_yaw_sbytes();
    s.write_i8(roll).write_i8(pitch).write_i8(yaw);
    s.write_u8(c.race_gender());
    s.write_pisc(&[0, 0, i64::from(c.appellations.active), 0]);
    s.write_pisc(&[
        i64::from(c.unit.faction_id),
        i64::from(c.unit.expedition_id),
        0,
        0,
    ]);
    s.write_pisc(&[
        i64::from(c.honor_gained_in_combat),
        i64::from(c.hostile_faction_kills),
    ]);
    s.write_u16(0); // flags (Invisible / IdleStatus are never set in this phase)

    let actives = c.active_abilities();
    for a in &c.abilities.abilities {
        s.write_i32(a.exp).write_u8(a.order);
    }
    s.write_u8(actives.len() as u8);
    for a in actives {
        s.write_u8(a);
    }
    s.write_bc(0);
    let Some(()) = c.visual_options.as_ref().and_then(|v| v.write_options(s)) else {
        tracing::error!(
            "Error writing PacketMarshaler: SCUnitStatePacket (no visual options / Stp)"
        );
        return;
    };
    s.write_i32(1); // premium
    for _ in 0..6 {
        s.write_i32(0); // pStat
    }

    // NetBuff
    for id in UNIT_STATE_FORCED_BUFFS {
        if c.unit.buffs.check_buff(id) {
            continue;
        }
        let Some(t) = data.skills.buff(id) else {
            tracing::error!(
                "Error writing PacketMarshaler: SCUnitStatePacket (no buff template {id})"
            );
            return;
        };
        let caster = SkillCaster::default(); // SkillCaster.GetByType(Unit)
        let b = Buff::new(t.clone(), caster, Some((c.id, c.unit.level)), now_ms);
        c.unit.buffs.add_buff(b, 0, 0, now_ms);
    }
    let (good, bad, hidden) = match c.unit.buffs.all_buffs(false) {
        Ok(v) => v,
        Err(kind) => {
            tracing::error!(
                "Error writing PacketMarshaler: SCUnitStatePacket (buff kind {kind}, {:?})",
                BuffKind::Other(kind)
            );
            return;
        }
    };
    for list in [good, bad, hidden] {
        s.write_u8(list.len() as u8);
        for b in list {
            b.write_net(s, now_ms);
        }
    }
}

/// The `BaseUnitType`-specific header of `NetUnit` (after the type byte).
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum NetUnitHeader {
    Npc {
        template_id: u32,
        owner_id: u32,
    },
    Slave {
        id: u32,
        tl_id: u16,
        template_id: u32,
        summoner_id: u32,
    },
    Housing {
        tl_id: u16,
        template_id: u32,
        build_step: i16,
    },
    Transfer {
        tl_id: u16,
        template_id: u32,
    },
    Mate {
        tl_id: u16,
        template_id: u32,
        owner_id: u32,
    },
    Shipyard {
        id: u64,
        template_id: u32,
    },
}

impl NetUnitHeader {
    fn base_unit_type(&self) -> BaseUnitType {
        match self {
            NetUnitHeader::Npc { .. } => BaseUnitType::Npc,
            NetUnitHeader::Slave { .. } => BaseUnitType::Slave,
            NetUnitHeader::Housing { .. } => BaseUnitType::Housing,
            NetUnitHeader::Transfer { .. } => BaseUnitType::Transfer,
            NetUnitHeader::Mate { .. } => BaseUnitType::Mate,
            NetUnitHeader::Shipyard { .. } => BaseUnitType::Shipyard,
        }
    }
}

/// How `Inventory_Equip` writes the non-body-part slots for a `BaseUnitType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EquipMode {
    /// Character / Housing / Mate / Slave: `stream.Write(item)` or `0`.
    Full,
    /// Npc: `templateId, id, grade` or `0`.
    Npc,
    /// Transfer / Shipyard: only the 7 body-part slots (19..=25).
    BodyOnly,
}

/// `AttachPoint1` of `SCUnitStatePacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AttachPoint1 {
    /// `(byte)AttachPointKind.System`.
    System,
    /// Slave: `AttachPointId` (sbyte) + `OwnerObjId` (bc) when attached.
    Slave { point: i8, owner_obj_id: u32 },
    /// Transfer: `AttachPointId` + `BondingObjId` (bc) unless `System`.
    Transfer { point: u8, bonding_obj_id: u32 },
}

/// Everything `SCUnitStatePacket.Write` reads from a non-character unit besides [`UnitBase`].
#[derive(Debug, Clone)]
pub struct NetUnitSpec<'a> {
    pub header: NetUnitHeader,
    pub equipment: &'a [Option<Item>],
    pub equip_mode: EquipMode,
    pub attach1: AttachPoint1,
    /// Slave `BondingObjId` (written as bc when > 0, else `-1`); 0 for the other kinds.
    pub bonding_obj_id: u32,
    pub posture: ModelPostureType,
    /// `Npc.AnimActionId` (`Unit.ModelPosture` animation override).
    pub anim_action_id: u32,
    pub race_gender: u8,
}

/// `Inventory_Equip(stream, unit, baseUnitType)`.
fn write_inventory_equip(
    s: &mut PacketStream,
    unit: &mut UnitBase,
    equipment: &[Option<Item>],
    mode: EquipMode,
) {
    let any = equipment.iter().take(32).any(Option::is_some);
    if !any && mode == EquipMode::Npc {
        // ModelParams.SetType(Skin): "the NPC has no body and no face".
        unit.model_params.kind = UnitCustomModelType::SKIN;
    }
    for index in 0..28usize {
        let item = equipment.get(index).and_then(Option::as_ref);
        if !(19..=25).contains(&index) {
            if index != 27 {
                match (mode, item) {
                    (EquipMode::BodyOnly, _) => {}
                    (EquipMode::Full, Some(item)) => item.write(s),
                    (EquipMode::Npc, Some(item)) => {
                        s.write_u32(item.template_id)
                            .write_u64(item.id)
                            .write_u8(item.grade);
                    }
                    (_, None) => {
                        s.write_i32(0);
                    }
                }
            } else {
                if mode == EquipMode::BodyOnly {
                    return;
                }
                match item {
                    Some(item) => item.write(s), // Cosplay [27]
                    None => {
                        s.write_i32(0);
                    }
                }
            }
        } else {
            s.write_u32(item.map_or(0, |i| i.template_id));
        }
    }
}

/// `Unit.ModelPosture(stream, unit, animActionId, activateAnimation: true)`.
pub fn write_model_posture(
    s: &mut PacketStream,
    posture: ModelPostureType,
    has_loot_left: bool,
    anim_action_id: u32,
) {
    s.write_u8(posture as u8).write_bool(has_loot_left);
    match posture {
        ModelPostureType::HouseState => {
            for _ in 0..2 {
                s.write_bool(true); // door
            }
            for _ in 0..6 {
                s.write_bool(true); // window
            }
        }
        ModelPostureType::ActorModelState => {
            s.write_u32(anim_action_id).write_bool(true);
        }
        ModelPostureType::FarmfieldState => {
            s.write_u32(0)
                .write_f32(0.0)
                .write_i32(0)
                .write_bool(false)
                .write_bool(false);
        }
        ModelPostureType::TurretState => {
            s.write_f32(0.0).write_f32(0.0); // pitch, yaw
        }
        ModelPostureType::None => {}
    }
}

/// `SCUnitStatePacket(unit).Write` for every `BaseUnitType` but `Character`.
/// `owner_name` = `NameManager.GetCharacterName(OwnerId) ?? ""` (only read when
/// `OwnerId > 0`). A buff of an unknown kind truncates the packet like the C# exception.
pub fn write_net_unit(
    s: &mut PacketStream,
    obj_id: u32,
    unit: &mut UnitBase,
    spec: &NetUnitSpec<'_>,
    owner_name: &str,
    now_ms: i64,
) {
    s.write_bc(obj_id)
        .write_str(&unit.name)
        .write_u8(spec.header.base_unit_type() as u8);
    match spec.header {
        NetUnitHeader::Npc {
            template_id,
            owner_id,
        } => {
            s.write_bc(obj_id)
                .write_u32(template_id)
                .write_u32(owner_id)
                .write_u8(0); // clientDriven
        }
        NetUnitHeader::Slave {
            id,
            tl_id,
            template_id,
            summoner_id,
        } => {
            s.write_u32(id)
                .write_u16(tl_id)
                .write_u32(template_id)
                .write_u32(summoner_id);
        }
        NetUnitHeader::Housing {
            tl_id,
            template_id,
            build_step,
        } => {
            s.write_u16(tl_id)
                .write_u32(template_id)
                .write_i16(build_step);
        }
        NetUnitHeader::Transfer { tl_id, template_id } => {
            s.write_u16(tl_id).write_u32(template_id);
        }
        NetUnitHeader::Mate {
            tl_id,
            template_id,
            owner_id,
        } => {
            s.write_u16(tl_id)
                .write_u32(template_id)
                .write_u32(owner_id);
        }
        NetUnitHeader::Shipyard { id, template_id } => {
            s.write_u64(id).write_u32(template_id);
        }
    }
    s.write_str(if unit.owner_id > 0 { owner_name } else { "" });
    // Npc: `Transform.Local.SetHeight(WorldManager.GetReferenceHeight(..))` is applied by
    // `npc_unit_state` before writing (`systems::npcs::heightmap::adjust_npc_reference_height`).
    let t = unit.transform;
    s.write_position(t.x, t.y, t.z)
        .write_f32(unit.scale)
        .write_u8(unit.level)
        .write_u32(unit.model_id);
    write_inventory_equip(s, unit, spec.equipment, spec.equip_mode);
    unit.model_params.write(s);
    s.write_bc(0)
        .write_i32(unit.hp.wrapping_mul(100))
        .write_i32(unit.mp.wrapping_mul(100));
    match spec.attach1 {
        AttachPoint1::System => {
            s.write_u8(attach_point::SYSTEM);
        }
        AttachPoint1::Slave {
            point,
            owner_obj_id,
        } => {
            s.write_i8(point);
            if point > -1 {
                s.write_bc(owner_obj_id);
            }
        }
        AttachPoint1::Transfer {
            point,
            bonding_obj_id,
        } => {
            s.write_u8(point);
            if point != attach_point::SYSTEM {
                s.write_bc(bonding_obj_id);
            }
        }
    }
    if spec.bonding_obj_id > 0 {
        s.write_bc(spec.bonding_obj_id);
    } else {
        s.write_i8(-1);
    }
    write_model_posture(s, spec.posture, unit.has_loot_left, spec.anim_action_id);
    s.write_u8(unit.active_weapon);
    s.write_u8(0).write_i32(0); // learnedSkillCount, learnedBuffCount
    let (roll, pitch, yaw) = t.roll_pitch_yaw_sbytes();
    s.write_i8(roll).write_i8(pitch).write_i8(yaw);
    s.write_u8(spec.race_gender);
    s.write_pisc(&[0, 0, 0, 0]);
    s.write_pisc(&[
        i64::from(unit.faction_id),
        i64::from(unit.expedition_id),
        0,
        0,
    ]);
    s.write_pisc(&[0, 0]);
    s.write_u16(0x2000); // flags
    let (good, bad, hidden) = match unit.buffs.all_buffs(false) {
        Ok(v) => v,
        Err(kind) => {
            tracing::error!(
                "Error writing PacketMarshaler: SCUnitStatePacket (buff kind {kind}, {:?})",
                BuffKind::Other(kind)
            );
            return;
        }
    };
    for list in [good, bad, hidden] {
        s.write_u8(list.len() as u8);
        for b in list {
            b.write_net(s, now_ms);
        }
    }
}

fn net_unit_packet(
    obj_id: u32,
    unit: &mut UnitBase,
    spec: &NetUnitSpec<'_>,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    let mut s = PacketStream::new();
    write_net_unit(&mut s, obj_id, unit, spec, owner_name, now_ms);
    SCUnitStatePacket {
        body: s.into_bytes(),
    }
}

/// `SCUnitStatePacket(npc)`.
pub fn npc_unit_state(
    obj_id: u32,
    npc: &mut Npc,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    crate::systems::npcs::heightmap::adjust_npc_reference_height(npc);
    let equipment = std::mem::take(&mut npc.equipment);
    let spec = NetUnitSpec {
        header: NetUnitHeader::Npc {
            template_id: npc.unit.template_id,
            owner_id: npc.unit.owner_id,
        },
        equipment: &equipment,
        equip_mode: EquipMode::Npc,
        attach1: AttachPoint1::System,
        bonding_obj_id: 0,
        posture: npc.model_posture_type(),
        anim_action_id: npc.anim_action_id,
        race_gender: npc.race_gender(),
    };
    let p = net_unit_packet(obj_id, &mut npc.unit, &spec, owner_name, now_ms);
    npc.equipment = equipment;
    p
}

/// `SCUnitStatePacket(slave)`.
pub fn slave_unit_state(
    obj_id: u32,
    slave: &mut Slave,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    let equipment = std::mem::take(&mut slave.equipment);
    let spec = NetUnitSpec {
        header: NetUnitHeader::Slave {
            id: slave.id,
            tl_id: slave.unit.tl_id,
            template_id: slave.unit.template_id,
            summoner_id: slave.summoner_id,
        },
        equipment: &equipment,
        equip_mode: EquipMode::Full,
        attach1: AttachPoint1::Slave {
            point: slave.attach_point_id,
            owner_obj_id: slave.owner_obj_id,
        },
        bonding_obj_id: slave.bonding_obj_id,
        posture: slave.model_posture_type(),
        anim_action_id: 0,
        race_gender: slave.race_gender(),
    };
    let p = net_unit_packet(obj_id, &mut slave.unit, &spec, owner_name, now_ms);
    slave.equipment = equipment;
    p
}

/// `SCUnitStatePacket(house)`.
pub fn house_unit_state(
    obj_id: u32,
    house: &mut House,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    let equipment = std::mem::take(&mut house.equipment);
    let spec = NetUnitSpec {
        header: NetUnitHeader::Housing {
            tl_id: house.unit.tl_id,
            template_id: house.unit.template_id,
            build_step: house.net_build_step(),
        },
        equipment: &equipment,
        equip_mode: EquipMode::Full,
        attach1: AttachPoint1::System,
        bonding_obj_id: 0,
        posture: house.model_posture_type(),
        anim_action_id: 0,
        race_gender: house.race_gender(),
    };
    let p = net_unit_packet(obj_id, &mut house.unit, &spec, owner_name, now_ms);
    house.equipment = equipment;
    p
}

/// `SCUnitStatePacket(transfer)` (only the 7 body-part slots are written).
pub fn transfer_unit_state(
    obj_id: u32,
    transfer: &mut Transfer,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    let spec = NetUnitSpec {
        header: NetUnitHeader::Transfer {
            tl_id: transfer.unit.tl_id,
            template_id: transfer.unit.template_id,
        },
        equipment: &[],
        equip_mode: EquipMode::BodyOnly,
        attach1: AttachPoint1::Transfer {
            point: transfer.attach_point_id,
            bonding_obj_id: transfer.bonding_obj_id,
        },
        bonding_obj_id: 0,
        posture: transfer.model_posture_type(),
        anim_action_id: 0,
        race_gender: transfer.race_gender(),
    };
    net_unit_packet(obj_id, &mut transfer.unit, &spec, owner_name, now_ms)
}

/// `SCUnitStatePacket(mate)`.
pub fn mate_unit_state(
    obj_id: u32,
    mate: &mut Mate,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    let equipment = std::mem::take(&mut mate.equipment);
    let spec = NetUnitSpec {
        header: NetUnitHeader::Mate {
            tl_id: mate.unit.tl_id,
            template_id: mate.unit.template_id,
            owner_id: mate.unit.owner_id,
        },
        equipment: &equipment,
        equip_mode: EquipMode::Full,
        attach1: AttachPoint1::System,
        bonding_obj_id: 0,
        posture: mate.model_posture_type(),
        anim_action_id: 0,
        race_gender: mate.race_gender(),
    };
    let p = net_unit_packet(obj_id, &mut mate.unit, &spec, owner_name, now_ms);
    mate.equipment = equipment;
    p
}

/// `SCUnitStatePacket(shipyard)` (only the 7 body-part slots are written).
pub fn shipyard_unit_state(
    obj_id: u32,
    shipyard: &mut Shipyard,
    owner_name: &str,
    now_ms: i64,
) -> SCUnitStatePacket {
    let spec = NetUnitSpec {
        header: NetUnitHeader::Shipyard {
            id: shipyard.data.id,
            template_id: shipyard.data.template_id,
        },
        equipment: &[],
        equip_mode: EquipMode::BodyOnly,
        attach1: AttachPoint1::System,
        bonding_obj_id: 0,
        posture: shipyard.model_posture_type(),
        anim_action_id: 0,
        race_gender: shipyard.race_gender(),
    };
    net_unit_packet(obj_id, &mut shipyard.unit, &spec, owner_name, now_ms)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::char_data::VisualOptions;
    use crate::models::unit_model::UnitCustomModelParams;

    #[test]
    fn state_layout_and_truncation() {
        let data = GameData::empty();
        let mut c = Character::new(5, 1, UnitCustomModelParams::default());
        c.unit.name = "Ab".into();
        c.obj_id = 0x100;
        (c.ability1, c.ability2, c.ability3) = (11, 11, 11);
        // No visual options: truncated right after the abilities' bc(0).
        let p = unit_state(&mut c, &data, 0);
        let mut model = PacketStream::new();
        c.unit.model_params.write(&mut model);
        let head = 3 + 4 + 1 + 4 + 8 + 2 + 9 + 4 + 1 + 4 + 28 * 4;
        let mid = model.len() + 25;
        let piscs = 5 + 5 + 3 + 2;
        let abilities = 10 * 5 + 1 + 3;
        assert_eq!(p.body.len(), head + mid + piscs + abilities);
        // With visual options: the forced buff templates are missing -> truncated before NetBuff.
        c.visual_options = Some(VisualOptions {
            flag: 1,
            stp: Some([0; 6]),
            ..VisualOptions::default()
        });
        let p = unit_state(&mut c, &data, 0);
        assert_eq!(
            p.body.len(),
            head + mid + piscs + abilities + 6 + 4 + 4 + 24
        );
        let cs = character_state(&c);
        assert_eq!(cs.body[4..6], [16, 0]);
        assert_eq!(cs.body[6..22], c.guid);
    }
}
