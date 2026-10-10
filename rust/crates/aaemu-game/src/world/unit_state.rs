//! Body builders for the packets whose C# `Write` reads a live `Character`:
//! `SCCharacterStatePacket`, `SCUnitStatePacket` (character branch) and
//! `SCCreateCharacterResponsePacket`.

use aaemu_commons::{Marshal, PacketStream};

use crate::data::skills::BuffKind;
use crate::data::GameData;
use crate::models::buff::{Buff, SkillCaster};
use crate::models::character::Character;
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
    s.write_u32(c.transform.instance_id) // instanceId
        .write_u16(16) // Guid: Write(byte[], appendSize: true)
        .write_raw(&c.guid)
        .write_i32(0); // rwd
    c.write(&mut s);
    s.write_f32(c.transform.rot_x)
        .write_f32(c.transform.rot_y)
        .write_f32(c.transform.rot_z)
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
        .write_str(&c.name)
        .write_u8(BASE_UNIT_TYPE_CHARACTER)
        .write_u32(c.id) // type(id)
        .write_i64(0) // v?
        .write_str(""); // OwnerId == 0 -> no master name
    s.write_position(c.transform.x, c.transform.y, c.transform.z)
        .write_f32(1.0) // Scale
        .write_u8(c.level)
        .write_u32(c.model_id);

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

    c.model_params.write(s);
    s.write_bc(0)
        .write_i32(c.hp.wrapping_mul(100)) // preciseHealth
        .write_i32(c.mp.wrapping_mul(100)) // preciseMana
        .write_u8(ATTACH_POINT_SYSTEM) // AttachPoint1
        .write_i8(-1); // AttachPoint2: no Bonding
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

    let (roll, pitch, yaw) = c.transform.roll_pitch_yaw_sbytes();
    s.write_i8(roll).write_i8(pitch).write_i8(yaw);
    s.write_u8(c.race_gender());
    s.write_pisc(&[0, 0, i64::from(c.appellations.active), 0]);
    s.write_pisc(&[i64::from(c.faction_id), i64::from(c.expedition_id), 0, 0]);
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
        if c.buffs.check_buff(id) {
            continue;
        }
        let Some(t) = data.skills.buff(id) else {
            tracing::error!(
                "Error writing PacketMarshaler: SCUnitStatePacket (no buff template {id})"
            );
            return;
        };
        let caster = SkillCaster::default(); // SkillCaster.GetByType(Unit)
        let b = Buff::new(t.clone(), caster, Some((c.id, c.level)), now_ms);
        c.buffs.add_buff(b, 0, 0, now_ms);
    }
    let (good, bad, hidden) = match c.buffs.all_buffs(false) {
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::char_data::VisualOptions;
    use crate::models::unit_model::UnitCustomModelParams;

    #[test]
    fn state_layout_and_truncation() {
        let data = GameData::empty();
        let mut c = Character::new(5, 1, UnitCustomModelParams::default());
        c.name = "Ab".into();
        c.obj_id = 0x100;
        (c.ability1, c.ability2, c.ability3) = (11, 11, 11);
        // No visual options: truncated right after the abilities' bc(0).
        let p = unit_state(&mut c, &data, 0);
        let mut model = PacketStream::new();
        c.model_params.write(&mut model);
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
