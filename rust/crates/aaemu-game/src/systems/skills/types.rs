//! Wire / value types of `Models/Game/Skills`: `SkillCaster` (+ subclasses), `SkillCastTarget`
//! (+ subclasses), `SkillObject` (+ subclasses), `CastAction`, `PlotObject` and the small enums
//! (`SkillResult`, `SkillHitType`, `DamageType`, `SkillTargetType`, ...). Reads / writes follow
//! the 1.2 C# `Read` / `Write` field for field.

use aaemu_commons::{MarshalError, PacketStream};

use crate::models::buff::SkillCaster as NetSkillCaster;

type ReadResult<T> = Result<T, MarshalError>;

/// `Helpers.ConvertLongX(float)` / `ConvertLongY(float)`.
pub fn convert_long(v: f32) -> i64 {
    ((v * 4096.0) as i64) << 32
}

/// `Helpers.ConvertLongX(long)` / `ConvertLongY(long)`.
pub fn convert_long_back(v: i64) -> f32 {
    (v >> 32) as f32 / 4096.0
}

// ---------------------------------------------------------------- SkillCaster

/// `SkillCasterType`.
pub mod caster_type {
    pub const UNIT: u8 = 0;
    pub const UNK1: u8 = 1;
    pub const ITEM: u8 = 2;
    pub const MOUNT: u8 = 3;
    pub const DOODAD: u8 = 4;
}

/// `SkillCaster` and its subclasses.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SkillCaster {
    /// `SkillCasterUnit`.
    Unit(u32),
    /// `SkillCasterUnk1`.
    Unk1(u32),
    /// `SkillItem` (`ObjId`, `ItemId`, `ItemTemplateId`, `Type1`, `Type2`).
    Item {
        obj_id: u32,
        item_id: u64,
        item_template_id: u32,
        type1: u8,
        type2: u32,
    },
    /// `SkillCasterMount`.
    Mount {
        obj_id: u32,
        mount_skill_template_id: u32,
    },
    /// `SkillDoodad`.
    Doodad(u32),
}

impl Default for SkillCaster {
    fn default() -> Self {
        SkillCaster::Unit(0)
    }
}

impl SkillCaster {
    /// `SkillCaster.GetByType(type)` + `Read`. `Err` for an unknown type (the C# throws
    /// `ArgumentOutOfRangeException`).
    pub fn read(kind: u8, s: &mut PacketStream) -> ReadResult<Self> {
        Ok(match kind {
            caster_type::UNIT => SkillCaster::Unit(s.read_bc()?),
            caster_type::UNK1 => SkillCaster::Unk1(s.read_bc()?),
            caster_type::ITEM => SkillCaster::Item {
                obj_id: s.read_bc()?,
                item_id: s.read_u64()?,
                item_template_id: s.read_u32()?,
                type1: s.read_u8()?,
                type2: s.read_u32()?,
            },
            caster_type::MOUNT => SkillCaster::Mount {
                obj_id: s.read_bc()?,
                mount_skill_template_id: s.read_u32()?,
            },
            caster_type::DOODAD => SkillCaster::Doodad(s.read_bc()?),
            other => {
                return Err(MarshalError::Invalid(format!(
                    "SkillCasterType {other} out of range"
                )))
            }
        })
    }

    pub fn kind(&self) -> u8 {
        match self {
            SkillCaster::Unit(_) => caster_type::UNIT,
            SkillCaster::Unk1(_) => caster_type::UNK1,
            SkillCaster::Item { .. } => caster_type::ITEM,
            SkillCaster::Mount { .. } => caster_type::MOUNT,
            SkillCaster::Doodad(_) => caster_type::DOODAD,
        }
    }

    pub fn obj_id(&self) -> u32 {
        match *self {
            SkillCaster::Unit(o) | SkillCaster::Unk1(o) | SkillCaster::Doodad(o) => o,
            SkillCaster::Item { obj_id, .. } | SkillCaster::Mount { obj_id, .. } => obj_id,
        }
    }

    pub fn set_obj_id(&mut self, id: u32) {
        match self {
            SkillCaster::Unit(o) | SkillCaster::Unk1(o) | SkillCaster::Doodad(o) => *o = id,
            SkillCaster::Item { obj_id, .. } | SkillCaster::Mount { obj_id, .. } => *obj_id = id,
        }
    }

    /// `SkillCaster.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind()).write_bc(self.obj_id());
        match *self {
            SkillCaster::Item {
                item_id,
                item_template_id,
                type1,
                type2,
                ..
            } => {
                s.write_u64(item_id)
                    .write_u32(item_template_id)
                    .write_u8(type1)
                    .write_u32(type2);
            }
            SkillCaster::Mount {
                mount_skill_template_id,
                ..
            } => {
                s.write_u32(mount_skill_template_id);
            }
            _ => {}
        }
    }

    /// The `(type, objId)` pair the phase-2 buff bookkeeping keeps.
    pub fn to_net(&self) -> NetSkillCaster {
        NetSkillCaster {
            kind: self.kind(),
            obj_id: self.obj_id(),
        }
    }

    /// Rebuilds a caster from the bookkeeping pair (item / mount extras are lost).
    pub fn from_net(c: NetSkillCaster) -> Self {
        match c.kind {
            caster_type::UNK1 => SkillCaster::Unk1(c.obj_id),
            caster_type::ITEM => SkillCaster::Item {
                obj_id: c.obj_id,
                item_id: 0,
                item_template_id: 0,
                type1: 0,
                type2: 0,
            },
            caster_type::MOUNT => SkillCaster::Mount {
                obj_id: c.obj_id,
                mount_skill_template_id: 0,
            },
            caster_type::DOODAD => SkillCaster::Doodad(c.obj_id),
            _ => SkillCaster::Unit(c.obj_id),
        }
    }

    pub fn is_item(&self) -> bool {
        matches!(self, SkillCaster::Item { .. })
    }
}

// ---------------------------------------------------------------- SkillCastTarget

/// `SkillCastTargetType`.
pub mod cast_target_type {
    pub const UNIT: u8 = 0;
    pub const POSITION: u8 = 1;
    pub const POSITION2: u8 = 2;
    pub const ITEM: u8 = 3;
    pub const DOODAD: u8 = 4;
    pub const POSITION3: u8 = 5;
}

/// `SkillCastTarget` and its subclasses.
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum SkillCastTarget {
    /// `SkillCastUnitTarget`.
    Unit(u32),
    /// `SkillCastPositionTarget` (`ObjId` stays 0 unless set by the skill code).
    Position {
        obj_id: u32,
        x: f32,
        y: f32,
        z: f32,
        rot: f32,
        obj_id1: u32,
        obj_id2: u32,
    },
    /// `SkillCastPosition2Target`.
    Position2 {
        obj_id: u32,
        x: f32,
        y: f32,
        z: f32,
        end: [f32; 3],
        norm: [f32; 3],
    },
    /// `SkillCastItemTarget`.
    Item {
        obj_id: u32,
        id: u64,
        type1: u32,
        type2: u8,
    },
    /// `SkillCastDoodadTarget`.
    Doodad(u32),
    /// `SkillCastPosition3Target`.
    Position3 {
        obj_id: u32,
        x: f32,
        y: f32,
        z: f32,
        pitch: f32,
    },
}

impl Default for SkillCastTarget {
    fn default() -> Self {
        SkillCastTarget::Unit(0)
    }
}

impl SkillCastTarget {
    /// `SkillCastTarget.GetByType(type)` + `Read`.
    pub fn read(kind: u8, s: &mut PacketStream) -> ReadResult<Self> {
        Ok(match kind {
            cast_target_type::UNIT => SkillCastTarget::Unit(s.read_bc()?),
            cast_target_type::POSITION => SkillCastTarget::Position {
                obj_id: 0,
                x: convert_long_back(s.read_i64()?),
                y: convert_long_back(s.read_i64()?),
                z: s.read_f32()?,
                rot: s.read_f32()?,
                obj_id1: s.read_bc()?,
                obj_id2: s.read_bc()?,
            },
            cast_target_type::POSITION2 => SkillCastTarget::Position2 {
                obj_id: 0,
                x: convert_long_back(s.read_i64()?),
                y: convert_long_back(s.read_i64()?),
                z: s.read_f32()?,
                end: [s.read_f32()?, s.read_f32()?, s.read_f32()?],
                norm: [s.read_f32()?, s.read_f32()?, s.read_f32()?],
            },
            cast_target_type::ITEM => SkillCastTarget::Item {
                obj_id: s.read_bc()?,
                id: s.read_u64()?,
                type1: s.read_u32()?,
                type2: s.read_u8()?,
            },
            cast_target_type::DOODAD => SkillCastTarget::Doodad(s.read_bc()?),
            cast_target_type::POSITION3 => SkillCastTarget::Position3 {
                obj_id: 0,
                x: convert_long_back(s.read_i64()?),
                y: convert_long_back(s.read_i64()?),
                z: s.read_f32()?,
                pitch: s.read_f32()?,
            },
            other => {
                return Err(MarshalError::Invalid(format!(
                    "SkillCastTargetType {other} out of range"
                )))
            }
        })
    }

    pub fn kind(&self) -> u8 {
        match self {
            SkillCastTarget::Unit(_) => cast_target_type::UNIT,
            SkillCastTarget::Position { .. } => cast_target_type::POSITION,
            SkillCastTarget::Position2 { .. } => cast_target_type::POSITION2,
            SkillCastTarget::Item { .. } => cast_target_type::ITEM,
            SkillCastTarget::Doodad(_) => cast_target_type::DOODAD,
            SkillCastTarget::Position3 { .. } => cast_target_type::POSITION3,
        }
    }

    pub fn obj_id(&self) -> u32 {
        match *self {
            SkillCastTarget::Unit(o) | SkillCastTarget::Doodad(o) => o,
            SkillCastTarget::Position { obj_id, .. }
            | SkillCastTarget::Position2 { obj_id, .. }
            | SkillCastTarget::Item { obj_id, .. }
            | SkillCastTarget::Position3 { obj_id, .. } => obj_id,
        }
    }

    pub fn set_obj_id(&mut self, id: u32) {
        match self {
            SkillCastTarget::Unit(o) | SkillCastTarget::Doodad(o) => *o = id,
            SkillCastTarget::Position { obj_id, .. }
            | SkillCastTarget::Position2 { obj_id, .. }
            | SkillCastTarget::Item { obj_id, .. }
            | SkillCastTarget::Position3 { obj_id, .. } => *obj_id = id,
        }
    }

    /// `targetCaster.Type is Unit or Doodad`.
    pub fn is_unit_or_doodad(&self) -> bool {
        matches!(self, SkillCastTarget::Unit(_) | SkillCastTarget::Doodad(_))
    }

    /// `SkillCastTarget.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind());
        match *self {
            SkillCastTarget::Unit(o) | SkillCastTarget::Doodad(o) => {
                s.write_bc(o);
            }
            SkillCastTarget::Position {
                x,
                y,
                z,
                rot,
                obj_id1,
                obj_id2,
                ..
            } => {
                s.write_i64(convert_long(x))
                    .write_i64(convert_long(y))
                    .write_f32(z)
                    .write_f32(rot)
                    .write_bc(obj_id1)
                    .write_bc(obj_id2);
            }
            SkillCastTarget::Position2 {
                x, y, z, end, norm, ..
            } => {
                s.write_i64(convert_long(x))
                    .write_i64(convert_long(y))
                    .write_f32(z);
                for v in end.iter().chain(norm.iter()) {
                    s.write_f32(*v);
                }
            }
            SkillCastTarget::Item {
                obj_id,
                id,
                type1,
                type2,
            } => {
                s.write_bc(obj_id)
                    .write_u64(id)
                    .write_u32(type1)
                    .write_u8(type2);
            }
            SkillCastTarget::Position3 { x, y, z, pitch, .. } => {
                s.write_i64(convert_long(x))
                    .write_i64(convert_long(y))
                    .write_f32(z)
                    .write_f32(pitch);
            }
        }
    }
}

// ---------------------------------------------------------------- SkillObject

/// `SkillObjectType`.
pub mod skill_object_type {
    pub const NONE: u8 = 0;
    pub const PORTAL_INFO: u8 = 1;
    pub const SAVE_PORTAL_INFO: u8 = 2;
    pub const TEXT: u8 = 3;
    pub const POSITION: u8 = 4;
    pub const UNK5: u8 = 5;
    pub const UNK6: u8 = 6;
    pub const ITEM_GRADE_ENCHANTING_SUPPORT: u8 = 7;
}

/// `SkillObject` and its subclasses.
#[derive(Debug, Clone, PartialEq, Default)]
pub enum SkillObject {
    #[default]
    None,
    /// `SkillObjectPortalInfo` (note: the C# reads / writes Y with `ConvertLongX` as well).
    PortalInfo {
        kind: u8,
        id: i32,
        x: f32,
        y: f32,
        z: f32,
    },
    SavePortalInfo {
        id: i32,
        name: String,
    },
    Text(String),
    Position {
        x: f32,
        y: f32,
        z: f32,
    },
    Unk5(i32),
    Unk6(String),
    ItemGradeEnchantingSupport {
        id: u32,
        support_item_id: u64,
        auto_use_aa_point: bool,
    },
}

impl SkillObject {
    /// `SkillObject.GetByType(flag)` + `Read` (only when `flag > 0`, as `CSStartSkillPacket`).
    /// Unknown flags fall back to the plain `SkillObject` (C# `default:`) but keep the flag.
    pub fn read(flag: u8, s: &mut PacketStream) -> ReadResult<Self> {
        Ok(match flag {
            skill_object_type::PORTAL_INFO => SkillObject::PortalInfo {
                kind: s.read_u8()?,
                id: s.read_i32()?,
                x: convert_long_back(s.read_i64()?),
                y: convert_long_back(s.read_i64()?),
                z: s.read_f32()?,
            },
            skill_object_type::SAVE_PORTAL_INFO => SkillObject::SavePortalInfo {
                id: s.read_i32()?,
                name: s.read_string()?,
            },
            skill_object_type::TEXT => SkillObject::Text(s.read_string()?),
            skill_object_type::POSITION => SkillObject::Position {
                x: convert_long_back(s.read_i64()?),
                y: convert_long_back(s.read_i64()?),
                z: s.read_f32()?,
            },
            skill_object_type::UNK5 => SkillObject::Unk5(s.read_i32()?),
            skill_object_type::UNK6 => SkillObject::Unk6(s.read_string()?),
            skill_object_type::ITEM_GRADE_ENCHANTING_SUPPORT => {
                SkillObject::ItemGradeEnchantingSupport {
                    id: s.read_u32()?,
                    support_item_id: s.read_u64()?,
                    auto_use_aa_point: s.read_bool()?,
                }
            }
            _ => SkillObject::None,
        })
    }

    pub fn flag(&self) -> u8 {
        match self {
            SkillObject::None => skill_object_type::NONE,
            SkillObject::PortalInfo { .. } => skill_object_type::PORTAL_INFO,
            SkillObject::SavePortalInfo { .. } => skill_object_type::SAVE_PORTAL_INFO,
            SkillObject::Text(_) => skill_object_type::TEXT,
            SkillObject::Position { .. } => skill_object_type::POSITION,
            SkillObject::Unk5(_) => skill_object_type::UNK5,
            SkillObject::Unk6(_) => skill_object_type::UNK6,
            SkillObject::ItemGradeEnchantingSupport { .. } => {
                skill_object_type::ITEM_GRADE_ENCHANTING_SUPPORT
            }
        }
    }

    /// `SkillObject.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.flag());
        match self {
            SkillObject::None => {}
            SkillObject::PortalInfo { kind, id, x, y, z } => {
                s.write_u8(*kind)
                    .write_i32(*id)
                    .write_i64(convert_long(*x))
                    .write_i64(convert_long(*y))
                    .write_f32(*z);
            }
            SkillObject::SavePortalInfo { id, name } => {
                s.write_i32(*id).write_str(name);
            }
            SkillObject::Text(t) | SkillObject::Unk6(t) => {
                s.write_str(t);
            }
            SkillObject::Position { x, y, z } => {
                s.write_i64(convert_long(*x))
                    .write_i64(convert_long(*y))
                    .write_f32(*z);
            }
            SkillObject::Unk5(step) => {
                s.write_i32(*step);
            }
            SkillObject::ItemGradeEnchantingSupport {
                id,
                support_item_id,
                auto_use_aa_point,
            } => {
                s.write_u32(*id)
                    .write_u64(*support_item_id)
                    .write_bool(*auto_use_aa_point);
            }
        }
    }
}

// ---------------------------------------------------------------- CastAction

/// `CastAction` and its subclasses.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CastAction {
    /// `CastSkill(skillId, tlId)`.
    Skill { skill_id: u32, tl_id: u16 },
    /// `CastPlot(plotId, tlId, eventId, skillId)`.
    Plot {
        plot_id: u32,
        tl_id: u16,
        event_id: u32,
        skill_id: u32,
    },
    /// `CastBuff(buff)`: `Template.BuffId`, `Owner.ObjId`, `Index`.
    Buff {
        buff_id: u32,
        owner_obj_id: u32,
        index: u32,
    },
    /// `CastUnk2`.
    BuffTarget,
    /// `CastUnk3`.
    DestroyTarget,
}

impl CastAction {
    /// `CastAction.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        match *self {
            CastAction::Skill { skill_id, tl_id } => {
                s.write_u8(0).write_u32(skill_id).write_u16(tl_id);
            }
            CastAction::Plot {
                plot_id,
                tl_id,
                event_id,
                skill_id,
            } => {
                s.write_u8(1)
                    .write_u32(plot_id)
                    .write_u16(tl_id)
                    .write_u32(event_id)
                    .write_u32(skill_id);
            }
            CastAction::Buff {
                buff_id,
                owner_obj_id,
                index,
            } => {
                s.write_u8(2)
                    .write_u32(buff_id)
                    .write_bc(owner_obj_id)
                    .write_u32(index)
                    .write_bool(true)
                    .write_bool(false);
            }
            CastAction::BuffTarget => {
                s.write_u8(3).write_i32(0).write_i32(0).write_bc(0);
            }
            CastAction::DestroyTarget => {
                s.write_u8(4).write_i32(0).write_bc(0);
            }
        }
    }
}

// ---------------------------------------------------------------- PlotObject

/// `PlotObject` (`UNIT` = obj id, `POSITION` = a position + sbyte rotation).
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum PlotObject {
    Unit(u32),
    Position {
        x: f32,
        y: f32,
        z: f32,
        roll: i8,
        pitch: i8,
        yaw: i8,
    },
}

impl PlotObject {
    /// `PlotObject.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        match *self {
            PlotObject::Unit(id) => {
                s.write_u8(1).write_bc(id);
            }
            PlotObject::Position {
                x,
                y,
                z,
                roll,
                pitch,
                yaw,
            } => {
                s.write_u8(2)
                    .write_position(x, y, z)
                    .write_i8(roll)
                    .write_i8(pitch)
                    .write_i8(yaw);
            }
        }
    }

    /// `PlotObject.UnitId` (0 for positions).
    pub fn unit_id(&self) -> u32 {
        match *self {
            PlotObject::Unit(id) => id,
            PlotObject::Position { .. } => 0,
        }
    }
}

// ---------------------------------------------------------------- enums

macro_rules! int_enum {
    ($(#[$m:meta])* $name:ident : $repr:ty { $($var:ident = $val:expr),* $(,)? }) => {
        $(#[$m])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
        #[repr($repr)]
        pub enum $name { $($var = $val),* }
        impl $name {
            /// The variant for a raw value (`None` when the C# enum lacks it).
            pub fn from_raw(v: i64) -> Option<Self> {
                $( if v == $val as i64 { return Some(Self::$var); } )*
                None
            }
        }
    };
}

int_enum!(
    /// `SkillHitType`.
    SkillHitType: u8 {
        Invalid = 0x0, MeleeHit = 0x1, MeleeCritical = 0x3, MeleeMiss = 0x4, MeleeDodge = 0x5,
        MeleeBlock = 0x6, MeleeParry = 0x7, RangedHit = 0x9, RangedMiss = 0xA,
        RangedCritical = 0xB, SpellHit = 0xD, SpellMiss = 0xE, SpellCritical = 0xF,
        RangedDodge = 0x10, RangedBlock = 0x11, Immune = 0x12, SpellResist = 0x13,
        RangedParry = 0x14,
    }
);

impl SkillHitType {
    /// The miss set of `Skill.SkillMissed` / `ConditionCombatDiceResult`.
    pub fn is_miss(self) -> bool {
        matches!(
            self,
            SkillHitType::MeleeDodge
                | SkillHitType::MeleeParry
                | SkillHitType::MeleeBlock
                | SkillHitType::MeleeMiss
                | SkillHitType::RangedDodge
                | SkillHitType::RangedParry
                | SkillHitType::RangedBlock
                | SkillHitType::RangedMiss
                | SkillHitType::SpellMiss
                | SkillHitType::SpellResist
                | SkillHitType::Immune
        )
    }
}

int_enum!(
    /// `DamageType`.
    DamageType: u8 { Melee = 1, Magic = 2, Siege = 3, Ranged = 4, Heal = 5 }
);

int_enum!(
    /// `SkillTargetType`.
    SkillTargetType: u8 {
        SelfTarget = 0, Friendly = 1, Party = 2, Raid = 3, Hostile = 4, AnyUnit = 5, Pos = 6,
        Line = 7, Doodad = 8, Item = 9, Pet = 10, BallisticPos = 11, SummonPos = 12,
        RelativePos = 13, SourcePos = 14, ArtilleryPos = 15, Others = 16, FriendlyOthers = 17,
        CursorPos = 18, Building = 19,
    }
);

int_enum!(
    /// `SkillTargetSelection`.
    SkillTargetSelection: u8 { Source = 1, Target = 2, Line = 3, Location = 4 }
);

int_enum!(
    /// `SkillTargetRelation`.
    SkillTargetRelation: u8 { Any = 0, Friendly = 1, Party = 2, Raid = 3, Hostile = 4, Others = 5 }
);

int_enum!(
    /// `SkillEffectApplicationMethod`.
    ApplicationMethod: u8 { Target = 1, Source = 2, SourceOnce = 3, SourceToPos = 4 }
);

int_enum!(
    /// `HealType`.
    HealType: u8 { Health = 0, Mana = 1 }
);

int_enum!(
    /// `HealHitType`.
    HealHitType: u8 { CriticalHealHit = 11, HealHit = 13 }
);

int_enum!(
    /// `KillReason`.
    KillReason: u8 {
        Unknown = 0x0, Damage = 0x1, PvpSiege = 0x2, PvpBattleField = 0x3, PvpHonorWar = 0x4,
        PvpEnemy = 0x5, PvpAlly = 0x6, CrimeDamage = 0x7, Fall = 0x8, Collide = 0x9,
        Capture = 0xA, Bomb = 0xB, NpcRemovalOnly = 0xC, Gm = 0xD, PortalTimeout = 0xE,
        PortalLinkBroken = 0xF, Drowning = 0x10, BadGuardTower = 0x11,
        SlaveEquipmentRandomDestroy = 0x12, SiegeEnd = 0x13,
    }
);

int_enum!(
    /// `BuffRemoveOn`.
    BuffRemoveOn: u8 {
        SourceDead = 0, UseSkill = 1, Move = 2, Death = 3, Exempt = 4, Land = 5,
        Interaction = 6, Unmount = 7, Mount = 8, StartSkill = 9, AttackSpellDot = 10,
        AttackEtcDot = 11, AttackBuffTrigger = 12, AttackEtc = 13, AttackedSpellDot = 14,
        AttackedEtcDot = 15, AttackedBuffTrigger = 16, AttackedEtc = 17, DamageSpellDot = 18,
        DamageEtcDot = 19, DamageBuffTrigger = 20, DamageEtc = 21, DamagedSpellDot = 22,
        DamagedEtcDot = 23, DamagedBuffTrigger = 24, DamagedEtc = 25, AutoAttack = 26,
    }
);

int_enum!(
    /// `BuffEventTriggerKind`.
    BuffEventTriggerKind: u16 {
        Attack = 1, Attacked = 2, Damage = 3, Damaged = 4, Dispelled = 5, Timeout = 6,
        DamagedMelee = 7, DamagedRanged = 8, DamagedSpell = 9, DamagedSiege = 10, Landing = 11,
        Started = 12, RemoveOnMove = 13, ChannelingCancel = 14, RemoveOnDamage = 15,
        Death = 16, Unmount = 17, Kill = 18, DamagedCollision = 19, Immotality = 20,
        Time = 21, KillAny = 22,
    }
);

int_enum!(
    /// `RelationState`.
    RelationState: u8 { Hostile = 1, Neutral = 2, Friendly = 3 }
);

/// `BuffStackRule`.
pub mod stack_rule {
    pub const REFRESH: u32 = 1;
    pub const CHARGE_REFRESH: u32 = 2;
    pub const CHARGE_EXTEND: u32 = 3;
    pub const MULTIPLE: u32 = 4;
    pub const EXTEND: u32 = 5;
    pub const INDEPENDENT: u32 = 6;
}

/// `BuffConstants`.
pub mod buff_constants {
    pub const ARMOR_BUFF_TAG: u32 = 145;
    pub const EQUIPMENT_BUFF_TAG: u32 = 156;
    pub const CLOTH_4P: u32 = 713;
    pub const CLOTH_7P: u32 = 714;
    pub const LEATHER_4P: u32 = 715;
    pub const LEATHER_7P: u32 = 716;
    pub const PLATE_4P: u32 = 717;
    pub const PLATE_7P: u32 = 740;
    pub const DUALWIELD_PROFICIENCY: u32 = 831;
    pub const FALL_STUN: u32 = 1391;
    pub const BLOODLUST: u32 = 1482;
    pub const RETRIBUTION: u32 = 2167;
    pub const LOGGED_ON: u32 = 2423;
    pub const DASH: u32 = 2675;
    pub const FORCIBLY_AWAITING_TRIAL: u32 = 3619;
    pub const WANTED: u32 = 3710;
    pub const CONTEMPTUOUS: u32 = 4832;
    pub const EQUIP_DUALWIELD: u32 = 4899;
    pub const EQUIP_SHIELD: u32 = 8226;
    pub const EQUIP_TWO_HANDED: u32 = 8227;
}

/// `TagsEnum` values used by the skill code.
pub mod tags {
    pub const STEALTH: u32 = 100;
    pub const RETURNING: u32 = 148;
    pub const NO_FIGHT: u32 = 345;
    /// `BuffConstants.TagWanted`.
    pub const WANTED: u32 = 894;
}

/// `SkillResult` (1.2 numbering, `SkillResult.cs`).
pub mod skill_result {
    pub const SUCCESS: u8 = 0x0;
    pub const FAILURE: u8 = 0x1;
    pub const SOURCE_DIED: u8 = 0x2;
    pub const SOURCE_ALIVE: u8 = 0x3;
    pub const TARGET_DIED: u8 = 0x4;
    pub const TARGET_DESTROYED: u8 = 0x5;
    pub const TARGET_ALIVE: u8 = 0x6;
    pub const ON_CASTING: u8 = 0x7;
    pub const COOLDOWN_TIME: u8 = 0x8;
    pub const NO_TARGET: u8 = 0x9;
    pub const LACK_HEALTH: u8 = 0xA;
    pub const LACK_MANA: u8 = 0xB;
    pub const OBSTACLE: u8 = 0xC;
    pub const OUTOF_HEIGHT: u8 = 0xD;
    pub const TOO_CLOSE_RANGE: u8 = 0xE;
    pub const TOO_FAR_RANGE: u8 = 0xF;
    pub const OUTOF_ANGLE: u8 = 0x10;
    pub const CANNOT_CAST_IN_COMBAT: u8 = 0x11;
    pub const CANNOT_CAST_WHILE_MOVING: u8 = 0x12;
    pub const CANNOT_CAST_IN_STUN: u8 = 0x13;
    pub const CANNOT_CAST_WHILE_WALKING: u8 = 0x14;
    pub const CANNOT_CAST_IN_SWIMMING: u8 = 0x15;
    pub const BLANK_MINDED: u8 = 0x16;
    pub const SILENCE: u8 = 0x17;
    pub const CRIPPLED: u8 = 0x18;
    pub const CANNOT_CAST_IN_CHANNELING: u8 = 0x19;
    pub const CANNOT_CAST_IN_PRISON: u8 = 0x1A;
    pub const NEED_STEALTH: u8 = 0x1B;
    pub const NEED_NOCOMBAT_TARGET: u8 = 0x1C;
    pub const TARGET_IMMUNE: u8 = 0x1D;
    pub const INVALID_SKILL: u8 = 0x1E;
    pub const INACTIVE_ABILITY: u8 = 0x1F;
    pub const NOT_ENOUGH_ABILITY_LEVEL: u8 = 0x20;
    pub const INVALID_SOURCE: u8 = 0x21;
    pub const INVALID_TARGET: u8 = 0x22;
    pub const INVALID_LOCATION: u8 = 0x23;
    pub const NEED_REAGENT: u8 = 0x24;
    pub const ITEM_LOCKED: u8 = 0x25;
    pub const NEED_MONEY: u8 = 0x26;
    pub const NEED_LABOR_POWER: u8 = 0x27;
    pub const NOT_NOW: u8 = 0x2C;
    pub const NO_PERM: u8 = 0x2D;
    pub const BAG_FULL: u8 = 0x2E;
    pub const SKILL_REQ_FAIL: u8 = 0x32;
}
