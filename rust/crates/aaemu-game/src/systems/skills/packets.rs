//! S->C packets of the skills subsystem (`AAEmu.Game.Core.Packets.G2C`), all level 1, plus
//! `CompressedGamePackets` (the level-4 `DD04` batch). Field order and widths follow the 1.2 C#
//! `Write`. `SCUnitPointsPacket` / `SCTargetChangedPacket` live in `packets::world*`.

use aaemu_commons::{Marshal, PacketStream};

use crate::net::packet::{PacketDef, ServerPacket, LEVEL_PLAIN};
use crate::opcodes::sc;
use crate::packets::packet_def;

use super::types::{
    CastAction, HealHitType, HealType, KillReason, PlotObject, SkillCastTarget, SkillCaster,
    SkillHitType, SkillObject,
};

/// `SCSkillStartedPacket` (`ExtraDataFlags`: 1 = byte (`SkillResult`), 2 = ushort, 4 = uint).
#[derive(Debug, Clone, PartialEq)]
pub struct SCSkillStartedPacket {
    pub id: u32,
    pub tl: u16,
    pub caster: SkillCaster,
    pub target: SkillCastTarget,
    pub skill_object: SkillObject,
    pub real_cast_time_div10: u16,
    pub base_cast_time_div10: u16,
    pub cast_synergy: u8,
    pub extra_flags: u8,
    pub extra_byte: u8,
    pub extra_ushort: u16,
    pub extra_uint: u32,
}
packet_def!(
    SCSkillStartedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillStartedPacket
);
impl SCSkillStartedPacket {
    pub fn new(
        id: u32,
        tl: u16,
        caster: SkillCaster,
        target: SkillCastTarget,
        skill_object: SkillObject,
    ) -> Self {
        Self {
            id,
            tl,
            caster,
            target,
            skill_object,
            real_cast_time_div10: 0,
            base_cast_time_div10: 0,
            cast_synergy: 0,
            extra_flags: 0,
            extra_byte: 0,
            extra_ushort: 0,
            extra_uint: 0,
        }
    }

    /// `SetSkillResult`.
    pub fn set_skill_result(&mut self, result: u8) -> &mut Self {
        if result != 0 {
            self.extra_flags |= 1;
        } else {
            self.extra_flags &= !1;
        }
        self.extra_byte = result;
        self
    }

    /// `SetResultUShort`.
    pub fn set_result_ushort(&mut self, v: u16) -> &mut Self {
        if v != 0 {
            self.extra_flags |= 2;
        } else {
            self.extra_flags &= !2;
        }
        self.extra_ushort = v;
        self
    }

    /// `SetResultUInt`.
    pub fn set_result_uint(&mut self, v: u32) -> &mut Self {
        if v != 0 {
            self.extra_flags |= 4;
        } else {
            self.extra_flags &= !4;
        }
        self.extra_uint = v;
        self
    }
}
impl Marshal for SCSkillStartedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_u16(self.tl);
        self.caster.write(s);
        self.target.write(s);
        self.skill_object.write(s);
        s.write_u16(self.real_cast_time_div10)
            .write_u16(self.base_cast_time_div10)
            .write_u8(self.cast_synergy)
            .write_u8(self.extra_flags);
        if self.extra_flags & 1 != 0 {
            s.write_u8(self.extra_byte);
        }
        if self.extra_flags & 2 != 0 {
            s.write_u16(self.extra_ushort);
        }
        if self.extra_flags & 4 != 0 {
            s.write_u32(self.extra_uint);
        }
    }
}

/// `SCSkillFiredPacket`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCSkillFiredPacket {
    pub id: u32,
    pub tl: u16,
    pub caster: SkillCaster,
    pub target: SkillCastTarget,
    pub skill_object: SkillObject,
    pub computed_delay: i16,
    /// `Skill.Template.ChannelingTime`.
    pub channeling_time: i32,
    /// `FireAnimId` (default: the template's `FireAnim?.Id`).
    pub fire_anim_id: u32,
}
packet_def!(
    SCSkillFiredPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillFiredPacket
);
impl Marshal for SCSkillFiredPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_u16(self.tl);
        self.caster.write(s);
        self.target.write(s);
        self.skill_object.write(s);
        s.write_i16((i32::from(self.computed_delay) / 10 + 10) as i16)
            .write_i16((self.channeling_time / 10 + 10) as i16)
            .write_u8(0) // f
            .write_u32(self.fire_anim_id)
            .write_u8(0); // flag
    }
}

/// `SCSkillEndedPacket(tlId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCSkillEndedPacket {
    pub tl: u16,
}
packet_def!(
    SCSkillEndedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillEndedPacket
);
impl Marshal for SCSkillEndedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl);
    }
}

/// `SCSkillStoppedPacket(unitObjId, skillId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCSkillStoppedPacket {
    pub obj_id: u32,
    pub skill_id: u32,
}
packet_def!(
    SCSkillStoppedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillStoppedPacket
);
impl Marshal for SCSkillStoppedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u32(self.skill_id);
    }
}

/// `SCCastingStoppedPacket(tlId, duration)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCCastingStoppedPacket {
    pub tl: u16,
    pub duration: u32,
}
packet_def!(
    SCCastingStoppedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCastingStoppedPacket
);
impl Marshal for SCCastingStoppedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl).write_u32(self.duration);
    }
}

/// `SCBuffCreatedPacket(buff)`: the fields the C# reads from the live `Buff`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCBuffCreatedPacket {
    pub skill_caster: SkillCaster,
    /// `Caster?.Id ?? 0`.
    pub caster_id: u32,
    pub owner_obj_id: u32,
    pub index: u32,
    pub buff_id: u32,
    /// `Caster?.Level ?? 1`.
    pub caster_level: u8,
    pub ab_level: u32,
    /// `Skill.Template.Id` when the skill's toggle buff is this buff, else 0.
    pub skill_id: u32,
    pub charge: i32,
    pub duration: i32,
    /// `Template.Tick`.
    pub tick: f64,
}
packet_def!(
    SCBuffCreatedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBuffCreatedPacket
);
impl Marshal for SCBuffCreatedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.skill_caster.write(s);
        s.write_u32(self.caster_id)
            .write_bc(self.owner_obj_id)
            .write_u32(self.index)
            .write_u32(self.buff_id)
            .write_u8(self.caster_level)
            .write_i16(self.ab_level as i16)
            .write_u32(self.skill_id);
        // Buff.WriteData: WritePisc(Charge, Duration / 10, 0, (long)(Template.Tick / 10))
        s.write_pisc(&[
            i64::from(self.charge),
            i64::from(self.duration / 10),
            0,
            (self.tick / 10.0) as i64,
        ]);
    }
}

/// `SCBuffRemovedPacket(objId, index)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCBuffRemovedPacket {
    pub obj_id: u32,
    pub index: u32,
}
packet_def!(
    SCBuffRemovedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBuffRemovedPacket
);
impl Marshal for SCBuffRemovedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u32(self.index);
    }
}

/// `SCBuffLearnedPacket(objId, buffId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCBuffLearnedPacket {
    pub obj_id: u32,
    pub buff_id: u32,
}
packet_def!(
    SCBuffLearnedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBuffLearnedPacket
);
impl Marshal for SCBuffLearnedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u32(self.buff_id);
    }
}

/// `SCUnitDamagedPacket`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCUnitDamagedPacket {
    pub cast_action: CastAction,
    pub skill_caster: SkillCaster,
    pub caster_id: u32,
    pub target_id: u32,
    pub damage: i32,
    pub absorbed: i32,
    pub mana_burn: i32,
    pub holdable_id: u8,
    pub hit_type: SkillHitType,
}
packet_def!(
    SCUnitDamagedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitDamagedPacket
);
impl SCUnitDamagedPacket {
    pub fn new(
        cast_action: CastAction,
        skill_caster: SkillCaster,
        caster_id: u32,
        target_id: u32,
        damage: i32,
        absorbed: i32,
    ) -> Self {
        Self {
            cast_action,
            skill_caster,
            caster_id,
            target_id,
            damage,
            absorbed,
            mana_burn: 0,
            holdable_id: 0,
            hit_type: SkillHitType::Invalid,
        }
    }
}
impl Marshal for SCUnitDamagedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.cast_action.write(s);
        self.skill_caster.write(s);
        s.write_bc(self.caster_id)
            .write_bc(self.target_id)
            .write_u8(0) // crimeState
            .write_pisc(&[i64::from(self.damage), i64::from(self.absorbed), 0])
            .write_pisc(&[0, 0, i64::from(self.mana_burn)])
            .write_u8(self.holdable_id)
            .write_u16(288 | self.hit_type as u16)
            .write_u8(1) // flag
            .write_u8(1); // result
    }
}

/// `SCUnitHealedPacket`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCUnitHealedPacket {
    pub cast_action: CastAction,
    pub skill_caster: SkillCaster,
    pub target_id: u32,
    pub heal_type: HealType,
    pub heal_hit_type: HealHitType,
    pub value: i32,
}
packet_def!(
    SCUnitHealedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitHealedPacket
);
impl Marshal for SCUnitHealedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.cast_action.write(s);
        self.skill_caster.write(s);
        s.write_bc(self.target_id)
            .write_u8(self.heal_type as u8)
            .write_u8(self.heal_hit_type as u8)
            .write_i32(self.value)
            .write_i32(0) // o
            .write_u8(1); // result
    }
}

/// `SCUnitDeathPacket`; `killer` = (obj id, name) of a killer unit.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCUnitDeathPacket {
    pub obj_id: u32,
    pub kill_reason: KillReason,
    pub resurrect_wait_time: i32,
    pub lost_exp: i32,
    pub durability_loss_rate: u8,
    pub killer: Option<(u32, String)>,
}
packet_def!(
    SCUnitDeathPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitDeathPacket
);
impl Marshal for SCUnitDeathPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u8(self.kill_reason as u8)
            .write_i32(self.resurrect_wait_time)
            .write_i32(self.lost_exp)
            .write_u8(self.durability_loss_rate)
            .write_bc(self.killer.as_ref().map_or(0, |k| k.0));
        if let Some((_, name)) = &self.killer {
            s.write_u8(0) // GameType
                .write_u16(0) // killStreak
                .write_u8(0)
                .write_u8(0)
                .write_u8(0)
                .write_str(name);
        }
    }
}

/// `SCCooldownsPacket` (always empty in the C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct SCCooldownsPacket;
packet_def!(
    SCCooldownsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCooldownsPacket
);
impl Marshal for SCCooldownsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(0).write_i32(0); // skillCount, tagCount
    }
}

/// `SCSkillCooldownResetPacket(chr, skillId, tagId, gcd)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCSkillCooldownResetPacket {
    pub obj_id: u32,
    pub skill_id: u32,
    pub tag_id: u32,
    pub gcd: bool,
}
packet_def!(
    SCSkillCooldownResetPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillCooldownResetPacket
);
impl Marshal for SCSkillCooldownResetPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u32(self.skill_id)
            .write_u32(self.tag_id)
            .write_bool(self.gcd);
    }
}

/// `SCPlotEventPacket`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCPlotEventPacket {
    pub tl: u16,
    pub event_id: u32,
    pub skill_id: u32,
    pub caster: PlotObject,
    pub target: PlotObject,
    pub obj_id: u32,
    pub casting_time: u16,
    pub flag: u8,
    pub item_id: u64,
    pub target_unit_count: u8,
}
packet_def!(
    SCPlotEventPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPlotEventPacket
);
impl Marshal for SCPlotEventPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl)
            .write_u32(self.event_id)
            .write_u32(self.skill_id);
        self.caster.write(s);
        self.target.write(s);
        s.write_u64(self.item_id)
            .write_bc(self.obj_id)
            .write_u16(self.casting_time)
            .write_bc(0)
            .write_i16(0)
            .write_u8(self.target_unit_count);
        for _ in 0..self.target_unit_count {
            s.write_bc(self.target.unit_id());
        }
        s.write_u8(self.flag);
        if (self.flag >> 3) & 1 != 1 {
            return;
        }
        for _ in 0..13 {
            s.write_i32(0);
        }
    }
}

/// `SCPlotEndedPacket(tl)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCPlotEndedPacket {
    pub tl: u16,
}
packet_def!(
    SCPlotEndedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPlotEndedPacket
);
impl Marshal for SCPlotEndedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl);
    }
}

/// `SCPlotCastingStoppedPacket(tl, duration, lastEvent)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCPlotCastingStoppedPacket {
    pub tl: u16,
    pub duration: u32,
    pub last_event: u8,
}
packet_def!(
    SCPlotCastingStoppedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPlotCastingStoppedPacket
);
impl Marshal for SCPlotCastingStoppedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl)
            .write_u32(self.duration)
            .write_u8(self.last_event);
    }
}

/// `SCPlotChannelingStoppedPacket(tl, duration, lastEvent)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCPlotChannelingStoppedPacket {
    pub tl: u16,
    pub duration: u32,
    pub last_event: u8,
}
packet_def!(
    SCPlotChannelingStoppedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPlotChannelingStoppedPacket
);
impl Marshal for SCPlotChannelingStoppedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl)
            .write_u32(self.duration)
            .write_u8(self.last_event);
    }
}

macro_rules! bc_packet {
    ($(#[$m:meta])* $name:ident, $op:expr) => {
        $(#[$m])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq)]
        pub struct $name {
            pub obj_id: u32,
        }
        packet_def!($name, level = LEVEL_PLAIN, opcode = $op);
        impl Marshal for $name {
            fn write(&self, s: &mut PacketStream) {
                s.write_bc(self.obj_id);
            }
        }
    };
}

bc_packet!(
    /// `SCCombatClearedPacket(objId)`.
    SCCombatClearedPacket,
    sc::SCCombatClearedPacket
);
bc_packet!(
    /// `SCCombatEngagedPacket(objId)`.
    SCCombatEngagedPacket,
    sc::SCCombatEngagedPacket
);

/// `SCCombatFirstHitPacket(vuId, huId, htId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCCombatFirstHitPacket {
    pub victim: u32,
    pub hitter: u32,
    pub hit_type: u32,
}
packet_def!(
    SCCombatFirstHitPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCombatFirstHitPacket
);
impl Marshal for SCCombatFirstHitPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.victim)
            .write_bc(self.hitter)
            .write_u32(self.hit_type);
    }
}

/// `SCAiAggroPacket(npcId, count, hostileUnitId, summarizeDamage, val2, val3)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAiAggroPacket {
    pub npc_id: u32,
    pub count: i32,
    pub hostile_unit_id: u32,
    pub summarize_damage: i32,
    pub val2: i32,
    pub val3: i32,
}
packet_def!(
    SCAiAggroPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAiAggroPacket
);
impl Marshal for SCAiAggroPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.npc_id).write_i32(self.count);
        if self.count > 0 {
            s.write_bc(self.hostile_unit_id)
                .write_i32(self.summarize_damage)
                .write_i32(self.val2)
                .write_i32(self.val3)
                .write_u8(135); // topFlags
        }
    }
}

/// `SCForceAttackSetPacket(objId, on)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCForceAttackSetPacket {
    pub obj_id: u32,
    pub on: bool,
}
packet_def!(
    SCForceAttackSetPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCForceAttackSetPacket
);
impl Marshal for SCForceAttackSetPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_bool(self.on);
    }
}

/// `SCUnitInvisiblePacket(objId, invisible)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnitInvisiblePacket {
    pub obj_id: u32,
    pub invisible: bool,
}
packet_def!(
    SCUnitInvisiblePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitInvisiblePacket
);
impl Marshal for SCUnitInvisiblePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_bool(self.invisible);
    }
}

/// `SCUnitFactionChangedPacket(unitId, unitName, id, id2, temp)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCUnitFactionChangedPacket {
    pub obj_id: u32,
    pub name: String,
    pub old_faction: u32,
    pub new_faction: u32,
    pub temp: bool,
}
packet_def!(
    SCUnitFactionChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitFactionChangedPacket
);
impl Marshal for SCUnitFactionChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_str(&self.name)
            .write_u32(self.old_faction)
            .write_u32(self.new_faction)
            .write_bool(self.temp);
    }
}

/// `EnvSource`.
pub mod env_source {
    pub const FALLING: u8 = 0;
    pub const GIMMICK: u8 = 1;
    pub const DROWNING: u8 = 2;
    /// Ship hull collisions (systems::ai physics).
    pub const COLLISION: u8 = 3;
}

/// `SCEnvDamagePacket(source, target, amount, gimmickId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCEnvDamagePacket {
    pub source: u8,
    pub target: u32,
    pub amount: u32,
    pub gimmick_id: u32,
}
packet_def!(
    SCEnvDamagePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCEnvDamagePacket
);
impl Marshal for SCEnvDamagePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.source)
            .write_bc(self.target)
            .write_u32(self.amount);
        if self.source == env_source::GIMMICK {
            s.write_u32(self.gimmick_id);
        }
    }
}

/// `SCCombatTextPacket(sourceUnitId, targetUnitId, textType)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCCombatTextPacket {
    pub source: u32,
    pub target: u32,
    pub text_type: u8,
}
packet_def!(
    SCCombatTextPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCombatTextPacket
);
impl Marshal for SCCombatTextPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.source)
            .write_bc(self.target)
            .write_u8(self.text_type);
    }
}

/// `SCSkillLearnedPacket(skill)` / `SCSkillUpgradedPacket(skill)`: `Id`, `Level`.
macro_rules! skill_level_packet {
    ($(#[$m:meta])* $name:ident, $op:expr) => {
        $(#[$m])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq)]
        pub struct $name {
            pub skill_id: u32,
            pub level: u8,
        }
        packet_def!($name, level = LEVEL_PLAIN, opcode = $op);
        impl Marshal for $name {
            fn write(&self, s: &mut PacketStream) {
                s.write_u32(self.skill_id).write_u8(self.level);
            }
        }
    };
}
skill_level_packet!(
    /// `SCSkillLearnedPacket`.
    SCSkillLearnedPacket,
    sc::SCSkillLearnedPacket
);
skill_level_packet!(
    /// `SCSkillUpgradedPacket`.
    SCSkillUpgradedPacket,
    sc::SCSkillUpgradedPacket
);

/// `SCSkillsResetPacket(objId, ability)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCSkillsResetPacket {
    pub obj_id: u32,
    pub ability: u8,
}
packet_def!(
    SCSkillsResetPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillsResetPacket
);
impl Marshal for SCSkillsResetPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u8(self.ability);
    }
}

/// `SCAbilityExpChangedPacket(objId, ability, exp)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAbilityExpChangedPacket {
    pub obj_id: u32,
    pub ability: u8,
    pub exp: i32,
}
packet_def!(
    SCAbilityExpChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAbilityExpChangedPacket
);
impl Marshal for SCAbilityExpChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u8(self.ability)
            .write_i32(self.exp);
    }
}

/// `SCExpChangedPacket(objId, exp, shouldAddAbilityExp)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCExpChangedPacket {
    pub obj_id: u32,
    pub exp: i32,
    pub should_add_ability_exp: bool,
}
packet_def!(
    SCExpChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpChangedPacket
);
impl Marshal for SCExpChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_i32(self.exp)
            .write_bool(self.should_add_ability_exp);
    }
}

/// `SCAbilitySwappedPacket(objId, oldAbilityId, abilityId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAbilitySwappedPacket {
    pub obj_id: u32,
    pub old_ability: u8,
    pub ability: u8,
}
packet_def!(
    SCAbilitySwappedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAbilitySwappedPacket
);
impl Marshal for SCAbilitySwappedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u8(self.old_ability)
            .write_u8(self.ability);
    }
}

/// `SCSkillControllerStatePacket(objId, scType, len, teared, cutouted)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCSkillControllerStatePacket {
    pub obj_id: u32,
    pub sc_type: u8,
    pub len: f32,
    pub teared: bool,
    pub cutouted: bool,
}
packet_def!(
    SCSkillControllerStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillControllerStatePacket
);
impl Marshal for SCSkillControllerStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u8(self.sc_type)
            .write_f32(self.len)
            .write_bool(self.teared)
            .write_bool(self.cutouted);
    }
}

/// `SCUnitFlyingStateChangedPacket(objId, isFlying)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnitFlyingStateChangedPacket {
    pub obj_id: u32,
    pub flying: bool,
}
packet_def!(
    SCUnitFlyingStateChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitFlyingStateChangedPacket
);
impl Marshal for SCUnitFlyingStateChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_bool(self.flying);
    }
}

/// `SCSetBreathPacket(breath)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCSetBreathPacket {
    pub breath: u32,
}
packet_def!(
    SCSetBreathPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSetBreathPacket
);
impl Marshal for SCSetBreathPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.breath);
    }
}

// ---------------------------------------------------------------- CompressedGamePackets

/// Level of `CompressedGamePackets` (`GamePacket(0, 4)`).
pub const LEVEL_COMPRESSED: u8 = 4;

/// `CompressedGamePackets`: `[dd][04][u16 count][deflate([u16 0][u16 typeId][body] ...)]`.
/// The `u16` after the level is the packet count, so it is this packet's "opcode" for
/// `net::packet::encode_raw`. The payload is a raw deflate stream; the C# uses
/// `DeflateStream(Optimal)`, here stored (uncompressed) deflate blocks are written, which every
/// inflater decodes to the same bytes (the compressed bytes themselves differ).
#[derive(Debug, Default)]
pub struct CompressedGamePackets {
    /// (opcode, body) per packet.
    pub packets: Vec<(u16, Vec<u8>)>,
}

impl CompressedGamePackets {
    pub fn new() -> Self {
        Self::default()
    }

    /// `AddPacket`.
    pub fn add(&mut self, packet: &dyn ServerPacket) {
        let mut body = PacketStream::new();
        packet.write_body(&mut body);
        self.packets.push((packet.opcode(), body.into_bytes()));
    }

    pub fn is_empty(&self) -> bool {
        self.packets.is_empty()
    }

    pub fn len(&self) -> usize {
        self.packets.len()
    }

    /// The uncompressed payload.
    pub fn raw_payload(&self) -> Vec<u8> {
        let mut s = PacketStream::new();
        for (op, body) in &self.packets {
            s.write_u16(0).write_u16(*op).write_raw(body);
        }
        s.into_bytes()
    }
}

/// Raw deflate with stored blocks (RFC 1951 BTYPE=00).
pub fn deflate_stored(data: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(data.len() + data.len() / 65535 * 5 + 5);
    let mut chunks = data.chunks(0xFFFF).peekable();
    if chunks.peek().is_none() {
        out.extend_from_slice(&[0x01, 0x00, 0x00, 0xFF, 0xFF]);
        return out;
    }
    while let Some(chunk) = chunks.next() {
        let last = chunks.peek().is_none();
        out.push(u8::from(last));
        let len = chunk.len() as u16;
        out.extend_from_slice(&len.to_le_bytes());
        out.extend_from_slice(&(!len).to_le_bytes());
        out.extend_from_slice(chunk);
    }
    out
}

/// Inflates a stored-block deflate stream (tests).
pub fn inflate_stored(data: &[u8]) -> Option<Vec<u8>> {
    let mut out = Vec::new();
    let mut i = 0;
    loop {
        let header = *data.get(i)?;
        if header & 0b110 != 0 {
            return None;
        }
        let len = u16::from_le_bytes([*data.get(i + 1)?, *data.get(i + 2)?]) as usize;
        out.extend_from_slice(data.get(i + 5..i + 5 + len)?);
        i += 5 + len;
        if header & 1 == 1 {
            return Some(out);
        }
    }
}

impl ServerPacket for CompressedGamePackets {
    fn opcode(&self) -> u16 {
        self.packets.len() as u16
    }
    fn level(&self) -> u8 {
        LEVEL_COMPRESSED
    }
    fn name(&self) -> &'static str {
        "CompressedGamePackets"
    }
    fn write_body(&self, stream: &mut PacketStream) {
        stream.write_raw(&deflate_stored(&self.raw_payload()));
    }
}

/// Encodes one packet body (helper for the raw-body packets and tests).
pub fn body_of<P: PacketDef + Marshal>(p: &P) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn body(p: &dyn ServerPacket) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write_body(&mut s);
        s.into_bytes()
    }

    #[test]
    fn skill_started_layout_with_error() {
        let mut p = SCSkillStartedPacket::new(
            0x0102_0304,
            0x0A0B,
            SkillCaster::Unit(0x112233),
            SkillCastTarget::Unit(0x445566),
            SkillObject::None,
        );
        p.real_cast_time_div10 = 150;
        p.base_cast_time_div10 = 150;
        p.set_skill_result(0x0F).set_result_uint(7);
        let mut e = PacketStream::new();
        e.write_u32(0x0102_0304)
            .write_u16(0x0A0B)
            .write_u8(0)
            .write_bc(0x112233)
            .write_u8(0)
            .write_bc(0x445566)
            .write_u8(0)
            .write_u16(150)
            .write_u16(150)
            .write_u8(0)
            .write_u8(5)
            .write_u8(0x0F)
            .write_u32(7);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &[0xDD, 0x01, 0, 0, 0xA1, 0x00]);
    }

    #[test]
    fn skill_fired_layout() {
        let p = SCSkillFiredPacket {
            id: 5,
            tl: 11,
            caster: SkillCaster::Unit(1),
            target: SkillCastTarget::Position {
                obj_id: 0,
                x: 1.0,
                y: 2.0,
                z: 3.0,
                rot: 0.5,
                obj_id1: 0,
                obj_id2: 0,
            },
            skill_object: SkillObject::None,
            computed_delay: 455,
            channeling_time: 2000,
            fire_anim_id: 87,
        };
        let mut e = PacketStream::new();
        e.write_u32(5)
            .write_u16(11)
            .write_u8(0)
            .write_bc(1)
            .write_u8(1);
        e.write_i64(4096i64 << 32)
            .write_i64(8192i64 << 32)
            .write_f32(3.0)
            .write_f32(0.5)
            .write_bc(0)
            .write_bc(0)
            .write_u8(0)
            .write_i16(55)
            .write_i16(210)
            .write_u8(0)
            .write_u32(87)
            .write_u8(0);
        assert_eq!(body(&p), e.into_bytes());
    }

    #[test]
    fn buff_created_layout() {
        let p = SCBuffCreatedPacket {
            skill_caster: SkillCaster::Unit(0x10),
            caster_id: 77,
            owner_obj_id: 0x20,
            index: 3,
            buff_id: 1482,
            caster_level: 50,
            ab_level: 30,
            skill_id: 0,
            charge: 0,
            duration: 30_000,
            tick: 1000.0,
        };
        let mut e = PacketStream::new();
        e.write_u8(0)
            .write_bc(0x10)
            .write_u32(77)
            .write_bc(0x20)
            .write_u32(3)
            .write_u32(1482)
            .write_u8(50)
            .write_i16(30)
            .write_u32(0)
            .write_pisc(&[0, 3000, 0, 100]);
        assert_eq!(body(&p), e.into_bytes());
    }

    #[test]
    fn unit_damaged_layout() {
        let mut p = SCUnitDamagedPacket::new(
            CastAction::Skill {
                skill_id: 2,
                tl_id: 10,
            },
            SkillCaster::Unit(5),
            5,
            6,
            300,
            40,
        );
        p.hit_type = SkillHitType::MeleeCritical;
        let mut e = PacketStream::new();
        e.write_u8(0)
            .write_u32(2)
            .write_u16(10)
            .write_u8(0)
            .write_bc(5)
            .write_bc(5)
            .write_bc(6)
            .write_u8(0)
            .write_pisc(&[300, 40, 0])
            .write_pisc(&[0, 0, 0])
            .write_u8(0)
            .write_u16(288 | 3)
            .write_u8(1)
            .write_u8(1);
        assert_eq!(body(&p), e.into_bytes());
    }

    #[test]
    fn heal_and_death_layout() {
        let h = SCUnitHealedPacket {
            cast_action: CastAction::Buff {
                buff_id: 9,
                owner_obj_id: 4,
                index: 2,
            },
            skill_caster: SkillCaster::Unit(4),
            target_id: 4,
            heal_type: HealType::Health,
            heal_hit_type: HealHitType::HealHit,
            value: 123,
        };
        let mut e = PacketStream::new();
        e.write_u8(2)
            .write_u32(9)
            .write_bc(4)
            .write_u32(2)
            .write_bool(true)
            .write_bool(false)
            .write_u8(0)
            .write_bc(4)
            .write_bc(4)
            .write_u8(0)
            .write_u8(13)
            .write_i32(123)
            .write_i32(0)
            .write_u8(1);
        assert_eq!(body(&h), e.into_bytes());

        let d = SCUnitDeathPacket {
            obj_id: 7,
            kill_reason: KillReason::Damage,
            resurrect_wait_time: 0,
            lost_exp: 0,
            durability_loss_rate: 0,
            killer: Some((9, "Bob".into())),
        };
        let mut e = PacketStream::new();
        e.write_bc(7)
            .write_u8(1)
            .write_i32(0)
            .write_i32(0)
            .write_u8(0)
            .write_bc(9)
            .write_u8(0)
            .write_u16(0)
            .write_u8(0)
            .write_u8(0)
            .write_u8(0)
            .write_str("Bob");
        assert_eq!(body(&d), e.into_bytes());
    }

    #[test]
    fn plot_event_layout() {
        let p = SCPlotEventPacket {
            tl: 12,
            event_id: 100,
            skill_id: 200,
            caster: PlotObject::Unit(1),
            target: PlotObject::Unit(2),
            obj_id: 0,
            casting_time: 0,
            flag: 2,
            item_id: 0,
            target_unit_count: 2,
        };
        let mut e = PacketStream::new();
        e.write_u16(12)
            .write_u32(100)
            .write_u32(200)
            .write_u8(1)
            .write_bc(1)
            .write_u8(1)
            .write_bc(2)
            .write_u64(0)
            .write_bc(0)
            .write_u16(0)
            .write_bc(0)
            .write_i16(0)
            .write_u8(2)
            .write_bc(2)
            .write_bc(2)
            .write_u8(2);
        assert_eq!(body(&p), e.into_bytes());
        let p8 = SCPlotEventPacket { flag: 8, ..p };
        assert_eq!(body(&p8).len(), body(&p).len() + 52);
    }

    #[test]
    fn small_packets() {
        assert_eq!(body(&SCSkillEndedPacket { tl: 0x1234 }), vec![0x34, 0x12]);
        assert_eq!(
            body(&SCBuffRemovedPacket {
                obj_id: 1,
                index: 2
            }),
            vec![1, 0, 0, 2, 0, 0, 0]
        );
        assert_eq!(
            body(&SCAiAggroPacket {
                npc_id: 1,
                count: 0,
                hostile_unit_id: 0,
                summarize_damage: 0,
                val2: 0,
                val3: 0
            }),
            vec![1, 0, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCAiAggroPacket {
                npc_id: 1,
                count: 1,
                hostile_unit_id: 2,
                summarize_damage: 3,
                val2: 0,
                val3: 0
            })
            .len(),
            3 + 4 + 3 + 12 + 1
        );
        assert_eq!(body(&SCCooldownsPacket), vec![0; 8]);
        assert_eq!(
            body(&SCCombatFirstHitPacket {
                victim: 1,
                hitter: 2,
                hit_type: 0
            }),
            vec![1, 0, 0, 2, 0, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCPlotCastingStoppedPacket {
                tl: 1,
                duration: 0,
                last_event: 1
            }),
            vec![1, 0, 0, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCEnvDamagePacket {
                source: env_source::FALLING,
                target: 3,
                amount: 10,
                gimmick_id: 9
            }),
            vec![0, 3, 0, 0, 10, 0, 0, 0]
        );
    }

    #[test]
    fn compressed_packets_roundtrip() {
        let mut c = CompressedGamePackets::new();
        c.add(&SCSkillEndedPacket { tl: 7 });
        c.add(&SCBuffRemovedPacket {
            obj_id: 1,
            index: 2,
        });
        let frame = encode(&c);
        assert_eq!(&frame[..4], &[0xDD, 0x04, 0x02, 0x00]);
        let raw = inflate_stored(&frame[4..]).unwrap();
        let mut e = PacketStream::new();
        e.write_u16(0)
            .write_u16(sc::SCSkillEndedPacket)
            .write_u16(7)
            .write_u16(0)
            .write_u16(sc::SCBuffRemovedPacket)
            .write_bc(1)
            .write_u32(2);
        assert_eq!(raw, e.into_bytes());
        let big = vec![7u8; 70_000];
        assert_eq!(inflate_stored(&deflate_stored(&big)).unwrap(), big);
        assert_eq!(
            inflate_stored(&deflate_stored(&[])).unwrap(),
            Vec::<u8>::new()
        );
    }
}
