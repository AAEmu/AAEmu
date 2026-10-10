//! The bookkeeping half of `Models/Game/Units/Buffs` + `Models/Game/Skills/Buff`: indices,
//! durations, stack rules (`Refresh` / `ChargeRefresh` / `MaxStack`), `GetAllBuffs`,
//! `CheckBuff`, persistence rules (`ShouldPersistBuff`) and the `NetBuff` wire format.
//!
//! Behaviour hooks: every effect that enters, is refreshed in or leaves `_effects` queues a
//! [`BuffEvent`]; the owner drains them with `systems::dispatch_buff_events` once its lock is
//! released, which hands them to the skills subsystem (`systems::UnitHooks::on_buff_event`).
//!
//! TODO(skills): buff effects and bonuses (`BuffTemplate.Start` / `Dispel`), effect / dispel
//! tasks (buffs never expire on their own here), tolerance counters, triggers,
//! `TriggerRemoveOn`, tag checks, modifier-cache updates, `SCBuffCreatedPacket` broadcasts.

use aaemu_commons::PacketStream;

use crate::data::skills::{save_rule, stack_rule, BuffKind, BuffTemplate};

/// `Buffs.GearBonusesIndex` / `FirstBuffIndex`.
pub const GEAR_BONUSES_INDEX: u32 = 1;
const FIRST_BUFF_INDEX: u32 = GEAR_BONUSES_INDEX + 1;
/// `Buffs.MinimumBuffDurationToSave`.
const MINIMUM_BUFF_DURATION_TO_SAVE: i32 = 60_000;

/// `BuffConstants.LoggedOn`.
pub const BUFF_LOGGED_ON: u32 = 2423;

/// `SkillCaster` as written by `SkillCaster.Write` (type + bc obj id). Only `SkillCasterUnit`
/// is used by this phase.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct SkillCaster {
    pub kind: u8,
    pub obj_id: u32,
}

/// `Buff`.
#[derive(Debug, Clone, PartialEq)]
pub struct Buff {
    pub index: u32,
    pub template: BuffTemplate,
    pub skill_caster: SkillCaster,
    /// `Caster?.Id` / `Caster?.Level` (`None` = no caster unit).
    pub caster: Option<(u32, u8)>,
    pub duration: i32,
    pub tick: f64,
    /// `StartTime` (unix ms).
    pub start_time_ms: i64,
    pub charge: i32,
    pub passive: bool,
    pub ab_level: u32,
    pub in_use: bool,
    /// `Skill?.Template.Id` (0 = not from a skill).
    pub skill_id: u32,
    // ---- runtime state of the skills subsystem (`systems::skills::buffs`) ----
    /// `State` (`EffectState`: 0 Created, 1 Acting, 2 Finishing, 3 Finished).
    pub state: u8,
    /// `_count` (remaining ticks, -1 = endless).
    pub count: i32,
    /// `Caster?.ObjId` (0 = no caster unit).
    pub caster_obj_id: u32,
    /// `Skill?.Level` (1 when not from a skill).
    pub skill_level: u8,
    /// `SkillCaster.Write` bytes when they differ from `(type, objId)` (item / mount casters).
    pub skill_caster_bytes: Vec<u8>,
    /// Timer generation: a scheduled dispel task only acts while it matches.
    pub task_gen: u64,
    /// `replace` argument of the pending `Exit` (passed on to `Dispel`).
    pub exit_replace: bool,
}

/// `EffectState`.
pub mod effect_state {
    pub const CREATED: u8 = 0;
    pub const ACTING: u8 = 1;
    pub const FINISHING: u8 = 2;
    pub const FINISHED: u8 = 3;
}

impl Buff {
    /// `new Buff(owner, caster, skillCaster, template, skill, time)`.
    pub fn new(
        template: BuffTemplate,
        skill_caster: SkillCaster,
        caster: Option<(u32, u8)>,
        now_ms: i64,
    ) -> Self {
        Self {
            index: 0,
            template,
            skill_caster,
            caster,
            duration: 0,
            tick: 0.0,
            start_time_ms: now_ms,
            charge: 0,
            passive: false,
            ab_level: 1,
            in_use: false,
            skill_id: 0,
            state: effect_state::CREATED,
            count: 0,
            caster_obj_id: skill_caster.obj_id,
            skill_level: 1,
            skill_caster_bytes: Vec::new(),
            task_gen: 0,
            exit_replace: false,
        }
    }

    /// `GetTimeLeft` (-1 for a permanent buff).
    pub fn time_left(&self, now_ms: i64) -> f64 {
        if self.duration == 0 {
            return -1.0;
        }
        let t = self.start_time_ms + i64::from(self.duration) - now_ms;
        t.max(0) as f64
    }

    /// `GetTimeElapsed`.
    pub fn time_elapsed(&self, now_ms: i64) -> u32 {
        (now_ms - self.start_time_ms) as u32
    }

    /// `SCUnitStatePacket.WriteBuff`.
    pub fn write_net(&self, s: &mut PacketStream, now_ms: i64) {
        s.write_u32(self.index)
            .write_u32(self.template.id)
            .write_u8(self.skill_caster.kind)
            .write_bc(self.skill_caster.obj_id)
            .write_u32(self.caster.map_or(0, |c| c.0))
            .write_u8(self.caster.map_or(1, |c| c.1))
            .write_i16(self.ab_level as i16)
            .write_i32(self.duration)
            .write_u32(self.time_elapsed(now_ms))
            .write_u32(self.tick as u32)
            .write_i32(0) // tickIndex
            .write_i32(1) // stack
            .write_i32(0) // charged
            .write_u32(0); // cooldownSkill
    }

    /// `Buffs.ShouldPersistBuff`.
    pub fn should_persist(&self, now_ms: i64) -> bool {
        if self.template.save_rule_id == save_rule::DONT_SAVE
            || self.passive
            || self.duration <= 0
            || self.time_left(now_ms) <= 0.0
        {
            return false;
        }
        if self.template.save_rule_id >= save_rule::CHARACTER_PERSISTENT {
            return true;
        }
        if self.template.save_rule_id == save_rule::NORMAL {
            if self.template.kind == BuffKind::Bad {
                return false;
            }
            if self.duration < MINIMUM_BUFF_DURATION_TO_SAVE {
                return false;
            }
            return true;
        }
        false
    }
}

/// A change of a unit's effect list, for the behaviour hooks (see the module docs).
#[derive(Debug, Clone, PartialEq)]
pub enum BuffEvent {
    /// A new effect entered `_effects` (C#: `OnBuffStarted`, `SkillModifiersCache` /
    /// `BuffModifiersCache.AddModifiers`, `CombatBuffs.AddCombatBuffs`, stun / silence /
    /// sleep -> `InterruptSkills`, then `SetInUse(true)` or `Template.Start`).
    Started(Buff),
    /// An existing effect was replaced by a stack rule (`last.OverwriteWith(buff)`); carries
    /// the new state (old index kept).
    Refreshed(Buff),
    /// An effect left `_effects` (C#: `Template.Dispel`, `SetInUse(false)`, `RemoveModifiers`,
    /// `CombatBuffs.RemoveCombatBuff`, gliding -> `TriggerRemoveOn(Land)`).
    Removed(Buff),
}

/// `Buffs` (one per unit).
#[derive(Debug, Clone, PartialEq)]
pub struct Buffs {
    pub effects: Vec<Buff>,
    next_index: u32,
    /// Pending [`BuffEvent`]s (drained by `systems::dispatch_buff_events`).
    events: Vec<BuffEvent>,
}

impl Default for Buffs {
    fn default() -> Self {
        Self {
            effects: Vec::new(),
            next_index: FIRST_BUFF_INDEX,
            events: Vec::new(),
        }
    }
}

impl Buffs {
    /// `CheckBuff(id)`.
    pub fn check_buff(&self, id: u32) -> bool {
        self.effects
            .iter()
            .any(|e| e.template.id > 0 && e.template.id == id)
    }

    /// Takes the pending [`BuffEvent`]s.
    pub fn take_events(&mut self) -> Vec<BuffEvent> {
        std::mem::take(&mut self.events)
    }

    /// Whether events are waiting to be dispatched.
    pub fn has_events(&self) -> bool {
        !self.events.is_empty()
    }

    fn remove_at(&mut self, i: usize) {
        let mut b = self.effects.remove(i);
        b.in_use = false; // SetInUse(false)
        self.events.push(BuffEvent::Removed(b));
    }

    /// `RemoveBuff(buffId)`: removes the first effect of that buff (the C# `break`s after one).
    pub fn remove_buff(&mut self, id: u32) {
        if let Some(i) = self.effects.iter().position(|e| e.template.id == id) {
            self.remove_at(i);
        }
    }

    /// `RemoveEffect(index)`.
    pub fn remove_effect_by_index(&mut self, index: u32) {
        if let Some(i) = self.effects.iter().position(|e| e.index == index) {
            self.remove_at(i);
        }
    }

    /// `RemoveEffect(templateId, skillId)` (every match).
    pub fn remove_effect_by_skill(&mut self, template_id: u32, skill_id: u32) {
        while let Some(i) = self
            .effects
            .iter()
            .position(|e| e.template.id == template_id && e.skill_id == skill_id)
        {
            self.remove_at(i);
        }
    }

    /// `RemoveAllEffects()` (bookkeeping; C# `Exit()`s each effect, which removes it).
    pub fn remove_all_effects(&mut self) {
        while !self.effects.is_empty() {
            self.remove_at(0);
        }
    }

    /// `GetEffectByIndex(index)`.
    pub fn effect_by_index(&self, index: u32) -> Option<&Buff> {
        self.effects.iter().find(|e| e.index == index)
    }

    /// `GetEffectByIndex(index)` for mutation (runtime state of the skills subsystem).
    pub fn effect_by_index_mut(&mut self, index: u32) -> Option<&mut Buff> {
        self.effects.iter_mut().find(|e| e.index == index)
    }

    /// `GetEffectFromBuffId(id)`.
    pub fn effect_from_buff_id(&self, id: u32) -> Option<&Buff> {
        self.effects.iter().find(|e| e.template.id == id)
    }

    /// `CheckBuffs(ids)`: any of them.
    pub fn check_buffs(&self, ids: &[u32]) -> bool {
        ids.iter().any(|&id| self.check_buff(id))
    }

    /// `GetBuffCountById(buffId)`.
    pub fn buff_count_by_id(&self, id: u32) -> i32 {
        self.count_by_id(id)
    }

    fn count_by_id(&self, id: u32) -> i32 {
        self.effects.iter().filter(|e| e.template.id == id).count() as i32
    }

    /// `AddBuff(Buff buff, index, forcedDuration)` (bookkeeping part).
    pub fn add_buff(&mut self, mut buff: Buff, index: u32, forced_duration: i32, now_ms: i64) {
        buff.duration = buff.template.get_duration(buff.ab_level);
        if forced_duration != 0 {
            buff.duration = forced_duration;
        }
        self.add_buff_with_duration(buff, index, now_ms);
    }

    /// Allocates the next buff index (`_nextIndex++`, wrapping past `GearBonusesIndex`).
    pub fn allocate_index(&mut self) -> u32 {
        let i = self.next_index;
        self.next_index = self.next_index.wrapping_add(1);
        if self.next_index == u32::MAX {
            self.next_index = FIRST_BUFF_INDEX;
        }
        i
    }

    /// The stack-rule part of `AddBuff` for a buff whose `index` (0 = allocate) and `duration`
    /// were already decided by the caller (duration modifiers, tolerance, forced duration).
    pub fn add_buff_with_duration(&mut self, mut buff: Buff, index: u32, now_ms: i64) {
        if index == 0 {
            buff.index = self.allocate_index();
        } else {
            buff.index = index;
        }
        // `StartTime == MinValue` never holds for buffs created with a time.
        let mut last: Option<usize> = None;
        match buff.template.stack_rule {
            stack_rule::REFRESH => {
                for (i, e) in self.effects.iter().enumerate() {
                    if e.in_use && e.template.id == buff.template.id {
                        if buff.time_left(now_ms) < e.time_left(now_ms) {
                            return;
                        }
                        last = Some(i);
                    }
                }
            }
            stack_rule::CHARGE_REFRESH => {
                for (i, e) in self.effects.iter().enumerate() {
                    if e.in_use && e.template.id == buff.template.id {
                        if buff.charge < e.charge {
                            return;
                        }
                        last = Some(i);
                    }
                }
            }
            _ => {
                if buff.template.max_stack > 0
                    && self.count_by_id(buff.template.id) >= buff.template.max_stack
                {
                    for (i, e) in self.effects.iter().enumerate() {
                        if e.in_use
                            && e.template.id == buff.template.id
                            && e.time_left(now_ms) < buff.time_left(now_ms)
                        {
                            last = Some(i);
                        }
                    }
                }
            }
        }
        match last {
            // `last.OverwriteWith(buff)`: charge, ab level, caster and skill caster are taken
            // over, the timer restarts (Extend adds the remaining time), everything else (index,
            // skill, template, passive flag) stays.
            Some(i) => {
                let e = &mut self.effects[i];
                let remaining = e.time_left(now_ms);
                e.charge = buff.charge;
                e.ab_level = buff.ab_level;
                e.caster = buff.caster;
                e.caster_obj_id = buff.caster_obj_id;
                e.skill_caster = buff.skill_caster;
                e.skill_caster_bytes = buff.skill_caster_bytes;
                e.start_time_ms = now_ms;
                e.duration = if e.template.stack_rule == stack_rule::EXTEND {
                    buff.duration.wrapping_add(remaining as i32)
                } else {
                    buff.duration
                };
                e.in_use = true;
                self.events
                    .push(BuffEvent::Refreshed(self.effects[i].clone()));
            }
            None => {
                buff.tick = buff.template.tick;
                buff.in_use = true;
                self.events.push(BuffEvent::Started(buff.clone()));
                self.effects.push(buff);
            }
        }
    }

    /// `GetAllBuffs(good, bad, hidden, includeAllPassives)`. `Err` for a kind the C# rejects
    /// (`NotSupportedException`).
    #[allow(clippy::type_complexity)]
    pub fn all_buffs(
        &self,
        include_all_passives: bool,
    ) -> Result<(Vec<&Buff>, Vec<&Buff>, Vec<&Buff>), i32> {
        let (mut good, mut bad, mut hidden) = (Vec::new(), Vec::new(), Vec::new());
        for b in &self.effects {
            match b.template.kind {
                BuffKind::Good => {
                    if b.passive && !include_all_passives {
                        continue;
                    }
                    good.push(b);
                }
                BuffKind::Bad => {
                    if b.passive && !include_all_passives {
                        continue;
                    }
                    bad.push(b);
                }
                BuffKind::Hidden => hidden.push(b),
                BuffKind::Other(k) => return Err(k),
            }
        }
        Ok((good, bad, hidden))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn indices_and_refresh_rule() {
        let mut b = Buffs::default();
        let mut t = BuffTemplate::simple(10, BuffKind::Good);
        t.duration = 1000;
        t.stack_rule = stack_rule::REFRESH;
        b.add_buff(
            Buff::new(t.clone(), SkillCaster::default(), None, 0),
            0,
            0,
            0,
        );
        assert_eq!(b.effects[0].index, 2);
        // A refresh with more time left replaces it and keeps the index.
        b.add_buff(
            Buff::new(t.clone(), SkillCaster::default(), None, 500),
            0,
            0,
            500,
        );
        assert_eq!(b.effects.len(), 1);
        assert_eq!(b.effects[0].index, 2);
        assert_eq!(b.effects[0].start_time_ms, 500);
        assert!(b.check_buff(10));
        let (g, bad, h) = b.all_buffs(false).unwrap();
        assert_eq!((g.len(), bad.len(), h.len()), (1, 0, 0));
        let ev = b.take_events();
        assert!(
            matches!(&ev[..], [BuffEvent::Started(s), BuffEvent::Refreshed(r)]
            if s.index == 2 && r.start_time_ms == 500)
        );
        assert!(!b.has_events());
    }

    #[test]
    fn removal_is_first_match_and_queues_events() {
        let mut b = Buffs::default();
        let t = BuffTemplate::simple(10, BuffKind::Good);
        for _ in 0..2 {
            b.add_buff(
                Buff::new(t.clone(), SkillCaster::default(), None, 0),
                0,
                0,
                0,
            );
        }
        b.take_events();
        b.remove_buff(10);
        assert_eq!(b.buff_count_by_id(10), 1);
        assert_eq!(b.effects[0].index, 3);
        b.remove_all_effects();
        let ev = b.take_events();
        assert!(
            matches!(&ev[..], [BuffEvent::Removed(x), BuffEvent::Removed(y)]
            if x.index == 2 && y.index == 3 && !y.in_use)
        );
    }

    #[test]
    fn net_buff_layout() {
        let t = BuffTemplate::simple(5, BuffKind::Hidden);
        let mut buff = Buff::new(
            t,
            SkillCaster {
                kind: 0,
                obj_id: 0x123456,
            },
            Some((7, 3)),
            1000,
        );
        buff.index = 9;
        let mut s = PacketStream::new();
        buff.write_net(&mut s, 1500);
        assert_eq!(
            s.len(),
            4 + 4 + 1 + 3 + 4 + 1 + 2 + 4 + 4 + 4 + 4 + 4 + 4 + 4
        );
        assert_eq!(s.as_bytes()[23..27], 500u32.to_le_bytes());
    }
}
