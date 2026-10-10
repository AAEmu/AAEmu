//! `Models/Game/Gimmicks/Gimmick` (physics props: elevators, rafts, ...): identity and the
//! `Write` format of `SCGimmicksCreatedPacket`. Movement handlers and spawners belong to the
//! gimmick subsystem.

use aaemu_commons::PacketStream;

use crate::impl_unit_data;
use crate::models::transform::{convert_long, PositionAndRotation};
use crate::models::unit::{unit_type_flag, BaseUnitType, UnitBase};

/// `Gimmick` (a `Unit`, but shown with the gimmick packets, never `SCUnitStatePacket`).
#[derive(Debug, Clone, PartialEq)]
pub struct Gimmick {
    pub unit: UnitBase,
    pub gimmick_id: u16,
    pub entity_guid: i64,
    /// `Template?.ModelPath ?? ""`.
    pub model_path: String,
    pub spawner_unit_id: u32,
    pub grasper_unit_id: u32,
    pub vel: [f32; 3],
    pub ang_vel: [f32; 3],
    pub scale_vel: f32,
    pub spawner_id: u32,
}

impl Gimmick {
    pub fn new(template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                ..UnitBase::default()
            },
            gimmick_id: 0,
            entity_guid: 0,
            model_path: String::new(),
            spawner_unit_id: 0,
            grasper_unit_id: 0,
            vel: [0.0; 3],
            ang_vel: [0.0; 3],
            scale_vel: 0.0,
            spawner_id: 0,
        }
    }

    /// `Gimmick.Write`. Note (C#): the obj id is a full `uint` here, not a bc; a null
    /// `Faction` throws in C# (the stored id is written here).
    pub fn write(&self, obj_id: u32, world: &PositionAndRotation, s: &mut PacketStream) {
        let q = world.to_quaternion();
        s.write_u32(obj_id)
            .write_u32(self.unit.template_id)
            .write_i64(self.entity_guid)
            .write_u32(self.unit.faction_id)
            .write_u32(self.spawner_unit_id)
            .write_u32(self.grasper_unit_id)
            .write_u32(self.unit.transform.zone_id)
            .write_str(&self.model_path)
            .write_i64(convert_long(world.x))
            .write_i64(convert_long(world.y))
            .write_f32(world.z)
            .write_f32(q.x)
            .write_f32(q.y)
            .write_f32(q.z)
            .write_f32(q.w)
            .write_f32(self.unit.scale)
            .write_f32(self.vel[0])
            .write_f32(self.vel[1])
            .write_f32(self.vel[2])
            .write_f32(self.ang_vel[0])
            .write_f32(self.ang_vel[1])
            .write_f32(self.ang_vel[2])
            .write_f32(self.scale_vel);
    }
}

// C#: `TypeFlag => None`, `BaseUnitType` not overridden (Invalid).
impl_unit_data!(Gimmick, BaseUnitType::Invalid, unit_type_flag::NONE);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gimmick_write_layout() {
        let mut g = Gimmick::new(44);
        g.entity_guid = -2;
        g.unit.faction_id = 1;
        g.unit.transform.zone_id = 100;
        g.model_path = "m".into();
        g.vel = [1.0, 2.0, 3.0];
        g.scale_vel = 0.5;
        let pos = PositionAndRotation::new(1.0, 2.0, 3.0, 0.0, 0.0, 0.0);
        let mut s = PacketStream::new();
        g.write(0x0102, &pos, &mut s);
        let mut e = PacketStream::new();
        e.write_u32(0x0102)
            .write_u32(44)
            .write_i64(-2)
            .write_u32(1)
            .write_u32(0)
            .write_u32(0)
            .write_u32(100)
            .write_str("m")
            .write_i64(4096 << 32)
            .write_i64(8192 << 32)
            .write_f32(3.0)
            .write_f32(0.0) // identity quaternion x, y, z, w
            .write_f32(0.0)
            .write_f32(0.0)
            .write_f32(1.0)
            .write_f32(1.0) // scale
            .write_f32(1.0)
            .write_f32(2.0)
            .write_f32(3.0)
            .write_f32(0.0)
            .write_f32(0.0)
            .write_f32(0.0)
            .write_f32(0.5);
        assert_eq!(s.as_bytes(), e.as_bytes());
    }
}
