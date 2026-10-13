//! Territory authority independent of packet/world objects. Callers validate the original
//! skill, current character/expedition membership, live tower, range and uninterrupted cast.
//! The authority rechecks its own participant, deadline and ownership generation at completion.
//!
//! Lock order for `*_with` callbacks is authority -> economy ItemTxn. The callback must not
//! call back into this manager, and ItemTxn holders must not acquire the authority lock.

use std::collections::BTreeMap;
use std::sync::{Mutex, MutexGuard};

/// Original 1.2 engraving skill 20866 channels for five minutes. Milliseconds are used for
/// channel completion; every persisted timestamp is Unix seconds.
pub const ENGRAVING_MIN_DURATION_MS: i64 = 5 * 60 * 1000;
/// Native 1.2 DominionUnkData has a fixed array of 100 character IDs per siege side.
pub const MAX_PARTICIPANTS_PER_SIDE: usize = 100;
/// Original 1.2 TAXRATE_CHANGE_LATER limits territory rate changes to once per hour.
pub const TAX_RATE_CHANGE_COOLDOWN_SECONDS: i64 = 60 * 60;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TowerIdentity {
    pub house_id: u32,
    pub instance_id: u32,
    pub guard_tower_setting_id: u32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct Treasury {
    pub cur_house_tax_money: i32,
    pub cur_hunt_tax_money: i32,
    pub peace_tax_money: i32,
    pub cur_house_tax_aa_point: i32,
    pub peace_tax_aa_point: i32,
}

impl Treasury {
    pub fn is_valid(self) -> bool {
        self.cur_house_tax_money >= 0
            && self.cur_hunt_tax_money >= 0
            && self.peace_tax_money >= 0
            && self.cur_house_tax_aa_point >= 0
            && self.peace_tax_aa_point >= 0
    }

    fn checked_add(self, credit: Self) -> Option<Self> {
        if !self.is_valid() || !credit.is_valid() {
            return None;
        }
        Some(Self {
            cur_house_tax_money: self
                .cur_house_tax_money
                .checked_add(credit.cur_house_tax_money)?,
            cur_hunt_tax_money: self
                .cur_hunt_tax_money
                .checked_add(credit.cur_hunt_tax_money)?,
            peace_tax_money: self.peace_tax_money.checked_add(credit.peace_tax_money)?,
            cur_house_tax_aa_point: self
                .cur_house_tax_aa_point
                .checked_add(credit.cur_house_tax_aa_point)?,
            peace_tax_aa_point: self
                .peace_tax_aa_point
                .checked_add(credit.peace_tax_aa_point)?,
        })
    }
}

/// Database state, not the native client's phase enumeration.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum SiegeStatus {
    Scheduled = 0,
    Active = 1,
    Finished = 2,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum SiegeOutcome {
    Pending = 0,
    DefenderHeld = 1,
    AttackerCaptured = 2,
    Cancelled = 3,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum SiegeSide {
    Attacker = 1,
    Defender = 2,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SiegeParticipant {
    pub character_id: u32,
    pub expedition_id: u32,
    pub side: SiegeSide,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SiegeState {
    pub siege_id: u64,
    pub attacker_expedition_id: u32,
    pub defender_expedition_id: u32,
    pub ownership_generation: u64,
    pub status: SiegeStatus,
    pub outcome: SiegeOutcome,
    pub scheduled_at: i64,
    pub starts_at: i64,
    pub ends_at: i64,
    pub ended_at: i64,
    pub participants: BTreeMap<u32, SiegeParticipant>,
}

impl SiegeState {
    pub fn active_at(&self, now_seconds: i64) -> bool {
        self.status != SiegeStatus::Finished
            && self.starts_at <= now_seconds
            && now_seconds < self.ends_at
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DominionState {
    pub zone_group_id: u32,
    pub tower: TowerIdentity,
    pub owner_expedition_id: u32,
    pub ownership_generation: u64,
    pub reign_start_at: i64,
    pub last_siege_end_at: i64,
    pub tax_rate: i32,
    pub national_tax_rate: u16,
    pub last_paid_at: i64,
    pub last_tax_rate_changed_at: i64,
    pub last_national_tax_rate_changed_at: i64,
    pub treasury: Treasury,
    /// Only authoritative tower death during an active siege enables engraving.
    pub tower_destroyed: bool,
    /// None means the template's initial/full HP; an active siege preserves partial damage.
    pub tower_hp: Option<i32>,
    /// Last explicit siege generation; retained when no siege is currently present.
    pub last_siege_id: u64,
    pub siege: Option<SiegeState>,
}

impl DominionState {
    pub fn unowned(zone_group_id: u32, tower: TowerIdentity) -> Self {
        Self {
            zone_group_id,
            tower,
            owner_expedition_id: 0,
            ownership_generation: 0,
            reign_start_at: 0,
            last_siege_end_at: 0,
            tax_rate: 0,
            national_tax_rate: 0,
            last_paid_at: 0,
            last_tax_rate_changed_at: 0,
            last_national_tax_rate_changed_at: 0,
            treasury: Treasury::default(),
            tower_destroyed: false,
            tower_hp: None,
            last_siege_id: 0,
            siege: None,
        }
    }

    pub fn validate(&self) -> Result<(), DominionError> {
        validate_identity(self.zone_group_id, self.tower)?;
        if !(0..=50).contains(&self.tax_rate) || !self.treasury.is_valid() {
            return Err(DominionError::InvalidTreasury);
        }
        if self.tower_hp.is_some_and(|hp| hp < 0)
            || (self.tower_destroyed && self.tower_hp != Some(0))
        {
            return Err(DominionError::InvalidState);
        }
        if [
            self.reign_start_at,
            self.last_siege_end_at,
            self.last_paid_at,
            self.last_tax_rate_changed_at,
            self.last_national_tax_rate_changed_at,
        ]
        .iter()
        .any(|t| *t < 0)
            || (self.owner_expedition_id == 0 && self.reign_start_at != 0)
            || (self.owner_expedition_id != 0 && self.ownership_generation == 0)
        {
            return Err(DominionError::InvalidState);
        }
        if let Some(siege) = &self.siege {
            if siege.siege_id == 0
                || siege.siege_id != self.last_siege_id
                || siege.attacker_expedition_id == 0
                || siege.defender_expedition_id == 0
                || siege.attacker_expedition_id == siege.defender_expedition_id
                || siege.ownership_generation == 0
                || siege.scheduled_at < 0
                || siege.starts_at < siege.scheduled_at
                || siege.ends_at <= siege.starts_at
                || siege.ends_at.checked_mul(1000).is_none()
                || (siege.status == SiegeStatus::Finished)
                    != (siege.outcome != SiegeOutcome::Pending)
                || (siege.status != SiegeStatus::Finished
                    && (siege.ended_at != 0
                        || siege.defender_expedition_id != self.owner_expedition_id
                        || siege.ownership_generation != self.ownership_generation))
                || (siege.status == SiegeStatus::Finished && siege.ended_at < siege.scheduled_at)
                || (self.tower_hp.is_some() && siege.status != SiegeStatus::Active)
            {
                return Err(DominionError::InvalidSiege);
            }
            for side in [SiegeSide::Attacker, SiegeSide::Defender] {
                if siege
                    .participants
                    .values()
                    .filter(|p| p.side == side)
                    .count()
                    > MAX_PARTICIPANTS_PER_SIDE
                {
                    return Err(DominionError::ParticipantLimit);
                }
            }
            for (id, participant) in &siege.participants {
                let expedition = match participant.side {
                    SiegeSide::Attacker => siege.attacker_expedition_id,
                    SiegeSide::Defender => siege.defender_expedition_id,
                };
                if *id == 0
                    || *id != participant.character_id
                    || participant.expedition_id != expedition
                {
                    return Err(DominionError::InvalidSiege);
                }
            }
        } else if self.tower_destroyed || self.tower_hp.is_some() {
            return Err(DominionError::InvalidSiege);
        }
        Ok(())
    }
}

#[derive(Debug, thiserror::Error, Clone, Copy, PartialEq, Eq)]
pub enum DominionError {
    #[error("unknown dominion")]
    Missing,
    #[error("invalid territory/tower/expedition/character identity")]
    InvalidIdentity,
    #[error("tower does not match this territory")]
    WrongTower,
    #[error("territory is already owned")]
    AlreadyOwned,
    #[error("invalid persisted territory authority")]
    InvalidState,
    #[error("invalid siege state")]
    InvalidSiege,
    #[error("siege deadlines are invalid")]
    InvalidSchedule,
    #[error("a siege is already scheduled or active")]
    SiegeInProgress,
    #[error("siege is not active")]
    SiegeNotActive,
    #[error("tower has not been destroyed during this siege")]
    TowerNotDestroyed,
    #[error("character is not a registered attacker in the designated expedition")]
    NotRegistered,
    #[error("participant side or expedition does not match this siege")]
    WrongSide,
    #[error("siege side has reached the native 100-character capacity")]
    ParticipantLimit,
    #[error("engraving is already in progress for this character")]
    AlreadyEngraving,
    #[error("engraving cannot finish before the siege deadline")]
    TooLate,
    #[error("engraving has not completed its minimum channel duration")]
    NotReady,
    #[error("engraving handle is stale, cancelled or belongs to another server session")]
    StaleHandle,
    #[error("territory is not owned by this expedition")]
    WrongOwner,
    #[error("resource operation failed")]
    Resources,
    #[error("invalid or overflowing treasury amount")]
    InvalidTreasury,
    #[error("territory tax rate cannot change before its one-hour cooldown expires")]
    TaxRateChangeLater,
    #[error("property tax quote no longer matches the territory owner or rate")]
    StaleTaxQuote,
    #[error("timestamp or generation overflow")]
    Overflow,
}

/// Only the authority can construct a handle. It is deliberately absent from DB rows.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EngravingHandle {
    pub zone_group_id: u32,
    pub siege_id: u64,
    pub character_id: u32,
    pub tower: TowerIdentity,
    pub started_at_ms: i64,
    pub ready_at_ms: i64,
    ownership_generation: u64,
    session_nonce: u128,
    channel_nonce: u64,
}

#[derive(Debug, Clone)]
pub(crate) struct SavedRow {
    pub revision: u64,
    pub state: DominionState,
}

/// A transaction's exact snapshots. A failed/replaced save never consumes another batch.
#[derive(Debug, Clone, Default)]
pub struct SaveBatch {
    session_nonce: u128,
    pub(crate) rows: Vec<SavedRow>,
    pub(crate) deleted: Vec<(u32, u64)>,
}

impl SaveBatch {
    pub fn changed_count(&self) -> usize {
        self.rows.len() + self.deleted.len()
    }
}

#[derive(Debug)]
struct Entry {
    state: DominionState,
    revision: u64,
    dirty: bool,
}

#[derive(Debug, Default)]
struct Inner {
    states: BTreeMap<u32, Entry>,
    removed: BTreeMap<u32, u64>,
    revision: u64,
    channel_nonce: u64,
    channels: BTreeMap<(u32, u32), EngravingHandle>,
    pending_save: Option<SaveBatch>,
}

#[derive(Debug)]
pub struct DominionManager {
    inner: Mutex<Inner>,
    session_nonce: u128,
}

impl Default for DominionManager {
    fn default() -> Self {
        Self {
            inner: Mutex::new(Inner::default()),
            session_nonce: rand::random(),
        }
    }
}

fn validate_identity(zone_group_id: u32, tower: TowerIdentity) -> Result<(), DominionError> {
    if zone_group_id == 0
        || zone_group_id > u16::MAX as u32
        || tower.house_id == 0
        || tower.guard_tower_setting_id == 0
    {
        return Err(DominionError::InvalidIdentity);
    }
    Ok(())
}

fn next_revision(inner: &Inner) -> Result<u64, DominionError> {
    inner.revision.checked_add(1).ok_or(DominionError::Overflow)
}

fn replace(inner: &mut Inner, state: DominionState, revision: u64) {
    inner.revision = revision;
    inner.removed.remove(&state.zone_group_id);
    inner.states.insert(
        state.zone_group_id,
        Entry {
            state,
            revision,
            dirty: true,
        },
    );
}

fn engraving_allowed(
    state: &DominionState,
    tower: TowerIdentity,
    character_id: u32,
    expedition_id: u32,
    now_seconds: i64,
) -> Result<(), DominionError> {
    if state.tower != tower {
        return Err(DominionError::WrongTower);
    }
    let siege = state.siege.as_ref().ok_or(DominionError::SiegeNotActive)?;
    if !siege.active_at(now_seconds)
        || siege.defender_expedition_id != state.owner_expedition_id
        || siege.ownership_generation != state.ownership_generation
    {
        return Err(DominionError::SiegeNotActive);
    }
    let participant = siege
        .participants
        .get(&character_id)
        .ok_or(DominionError::NotRegistered)?;
    if expedition_id == 0
        || participant.side != SiegeSide::Attacker
        || participant.expedition_id != expedition_id
        || siege.attacker_expedition_id != expedition_id
    {
        return Err(DominionError::NotRegistered);
    }
    if !state.tower_destroyed {
        return Err(DominionError::TowerNotDestroyed);
    }
    Ok(())
}

impl DominionManager {
    fn lock(&self) -> MutexGuard<'_, Inner> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    pub fn get(&self, zone_group_id: u32) -> Option<DominionState> {
        self.lock()
            .states
            .get(&zone_group_id)
            .map(|entry| entry.state.clone())
    }

    pub fn all(&self) -> Vec<DominionState> {
        self.lock()
            .states
            .values()
            .map(|entry| entry.state.clone())
            .collect()
    }

    pub fn state(&self, zone_group_id: u32) -> Option<DominionState> {
        self.get(zone_group_id)
    }

    pub fn snapshots(&self) -> Vec<DominionState> {
        self.all()
    }

    pub fn by_tower(&self, tower: TowerIdentity) -> Option<DominionState> {
        self.lock()
            .states
            .values()
            .find(|entry| entry.state.tower == tower)
            .map(|entry| entry.state.clone())
    }

    /// Validate the complete DB snapshot before replacing authority; no channel resumes.
    pub fn restore(&self, states: Vec<DominionState>) -> Result<(), DominionError> {
        let mut live = self.lock();
        let mut restored = Inner {
            revision: live.revision,
            channel_nonce: live.channel_nonce,
            ..Inner::default()
        };
        let mut towers = BTreeMap::new();
        for state in states {
            state.validate()?;
            if restored.states.contains_key(&state.zone_group_id)
                || towers
                    .insert(state.tower.house_id, state.zone_group_id)
                    .is_some()
            {
                return Err(DominionError::InvalidState);
            }
            let revision = next_revision(&restored)?;
            restored.revision = revision;
            restored.states.insert(
                state.zone_group_id,
                Entry {
                    state,
                    revision,
                    dirty: false,
                },
            );
        }
        // Reload never reuses a revision or cancelled channel nonce in this manager.
        *live = restored;
        Ok(())
    }

    pub fn register_tower(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
    ) -> Result<DominionState, DominionError> {
        validate_identity(zone_group_id, tower)?;
        let mut inner = self.lock();
        if let Some(existing) = inner.states.get(&zone_group_id) {
            return if existing.state.tower == tower {
                Ok(existing.state.clone())
            } else {
                Err(DominionError::WrongTower)
            };
        }
        if inner
            .states
            .values()
            .any(|e| e.state.tower.house_id == tower.house_id)
        {
            return Err(DominionError::WrongTower);
        }
        let state = DominionState::unowned(zone_group_id, tower);
        let revision = next_revision(&inner)?;
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    /// Pack validation/consumption must succeed while the same authority lock protects the
    /// claim. All fallible state work precedes the callback; no ownership is minted on failure.
    pub fn declare_unowned_with(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        expedition_id: u32,
        now_seconds: i64,
        consume: impl FnOnce() -> bool,
    ) -> Result<DominionState, DominionError> {
        validate_identity(zone_group_id, tower)?;
        if expedition_id == 0 || now_seconds < 0 {
            return Err(DominionError::InvalidIdentity);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .map(|e| e.state.clone())
            .unwrap_or_else(|| DominionState::unowned(zone_group_id, tower));
        if state.tower != tower
            || inner.states.values().any(|e| {
                e.state.zone_group_id != zone_group_id && e.state.tower.house_id == tower.house_id
            })
        {
            return Err(DominionError::WrongTower);
        }
        if state.owner_expedition_id != 0 {
            return Err(DominionError::AlreadyOwned);
        }
        let revision = next_revision(&inner)?;
        state.ownership_generation = state
            .ownership_generation
            .checked_add(1)
            .ok_or(DominionError::Overflow)?;
        state.owner_expedition_id = expedition_id;
        state.reign_start_at = now_seconds;
        if !consume() {
            return Err(DominionError::Resources);
        }
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn remove_tower(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
    ) -> Result<DominionState, DominionError> {
        let mut inner = self.lock();
        let state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if state.tower != tower {
            return Err(DominionError::WrongTower);
        }
        let revision = next_revision(&inner)?;
        inner.states.remove(&zone_group_id);
        inner.channels.retain(|(zone, _), _| *zone != zone_group_id);
        inner.removed.insert(zone_group_id, revision);
        inner.revision = revision;
        Ok(state)
    }

    pub fn schedule_siege(
        &self,
        zone_group_id: u32,
        attacker_expedition_id: u32,
        starts_at: i64,
        ends_at: i64,
        now_seconds: i64,
    ) -> Result<DominionState, DominionError> {
        if now_seconds < 0
            || starts_at < now_seconds
            || ends_at <= starts_at
            || ends_at.checked_mul(1000).is_none()
        {
            return Err(DominionError::InvalidSchedule);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if attacker_expedition_id == 0
            || state.owner_expedition_id == 0
            || attacker_expedition_id == state.owner_expedition_id
        {
            return Err(DominionError::InvalidIdentity);
        }
        if state
            .siege
            .as_ref()
            .is_some_and(|s| s.status != SiegeStatus::Finished)
        {
            return Err(DominionError::SiegeInProgress);
        }
        let siege_id = state
            .last_siege_id
            .checked_add(1)
            .ok_or(DominionError::Overflow)?;
        let revision = next_revision(&inner)?;
        state.last_siege_id = siege_id;
        state.tower_destroyed = false;
        state.tower_hp = None;
        state.siege = Some(SiegeState {
            siege_id,
            attacker_expedition_id,
            defender_expedition_id: state.owner_expedition_id,
            ownership_generation: state.ownership_generation,
            status: if starts_at == now_seconds {
                SiegeStatus::Active
            } else {
                SiegeStatus::Scheduled
            },
            outcome: SiegeOutcome::Pending,
            scheduled_at: now_seconds,
            starts_at,
            ends_at,
            ended_at: 0,
            participants: BTreeMap::new(),
        });
        inner.channels.retain(|(zone, _), _| *zone != zone_group_id);
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn register_participant(
        &self,
        zone_group_id: u32,
        character_id: u32,
        expedition_id: u32,
        side: SiegeSide,
        now_seconds: i64,
    ) -> Result<DominionState, DominionError> {
        if character_id == 0 || expedition_id == 0 || now_seconds < 0 {
            return Err(DominionError::InvalidIdentity);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        let siege = state.siege.as_mut().ok_or(DominionError::SiegeNotActive)?;
        if siege.status == SiegeStatus::Finished || now_seconds >= siege.ends_at {
            return Err(DominionError::SiegeNotActive);
        }
        let expected = match side {
            SiegeSide::Attacker => siege.attacker_expedition_id,
            SiegeSide::Defender => siege.defender_expedition_id,
        };
        if expedition_id != expected {
            return Err(DominionError::WrongSide);
        }
        let participant = SiegeParticipant {
            character_id,
            expedition_id,
            side,
        };
        if siege.participants.get(&character_id) == Some(&participant) {
            return Ok(state);
        }
        let count = siege
            .participants
            .values()
            .filter(|p| p.side == side && p.character_id != character_id)
            .count();
        if count >= MAX_PARTICIPANTS_PER_SIDE {
            return Err(DominionError::ParticipantLimit);
        }
        let revision = next_revision(&inner)?;
        siege.participants.insert(character_id, participant);
        inner.channels.remove(&(zone_group_id, character_id));
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn unregister_participant(
        &self,
        zone_group_id: u32,
        character_id: u32,
    ) -> Result<bool, DominionError> {
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        let Some(siege) = state.siege.as_mut() else {
            return Ok(false);
        };
        if !siege.participants.contains_key(&character_id) {
            return Ok(false);
        }
        let revision = next_revision(&inner)?;
        siege.participants.remove(&character_id);
        inner.channels.remove(&(zone_group_id, character_id));
        replace(&mut inner, state, revision);
        Ok(true)
    }

    pub fn allow_engraving(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        character_id: u32,
        expedition_id: u32,
        now_ms: i64,
    ) -> bool {
        if now_ms < 0 {
            return false;
        }
        self.lock().states.get(&zone_group_id).is_some_and(|entry| {
            engraving_allowed(
                &entry.state,
                tower,
                character_id,
                expedition_id,
                now_ms / 1000,
            )
            .is_ok()
                && entry.state.siege.as_ref().is_some_and(|siege| {
                    now_ms
                        .checked_add(ENGRAVING_MIN_DURATION_MS)
                        .is_some_and(|ready| ready < siege.ends_at * 1000)
                })
        })
    }

    /// Damage authorization before destruction does not grant capture authority.
    pub fn registered_attacker(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        character_id: u32,
        expedition_id: u32,
        now_seconds: i64,
    ) -> bool {
        if now_seconds < 0 || expedition_id == 0 {
            return false;
        }
        self.lock().states.get(&zone_group_id).is_some_and(|entry| {
            let state = &entry.state;
            state.tower == tower
                && state.siege.as_ref().is_some_and(|siege| {
                    siege.active_at(now_seconds)
                        && siege.defender_expedition_id == state.owner_expedition_id
                        && siege.ownership_generation == state.ownership_generation
                        && siege.attacker_expedition_id == expedition_id
                        && siege.participants.get(&character_id).is_some_and(|p| {
                            p.side == SiegeSide::Attacker && p.expedition_id == expedition_id
                        })
                })
        })
    }

    pub fn mark_tower_destroyed(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        now_seconds: i64,
    ) -> Result<DominionState, DominionError> {
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if state.tower != tower {
            return Err(DominionError::WrongTower);
        }
        if now_seconds < 0
            || !state.siege.as_ref().is_some_and(|s| {
                s.active_at(now_seconds)
                    && s.defender_expedition_id == state.owner_expedition_id
                    && s.ownership_generation == state.ownership_generation
            })
        {
            return Err(DominionError::SiegeNotActive);
        }
        if state.tower_destroyed {
            return Ok(state);
        }
        let revision = next_revision(&inner)?;
        state.tower_destroyed = true;
        state.tower_hp = Some(0);
        if let Some(siege) = state.siege.as_mut() {
            siege.status = SiegeStatus::Active;
        }
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn update_tower_hp(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        hp: i32,
    ) -> Result<DominionState, DominionError> {
        self.update_tower_hp_at(zone_group_id, tower, hp, chrono::Utc::now().timestamp())
    }

    pub fn update_tower_hp_at(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        hp: i32,
        now_seconds: i64,
    ) -> Result<DominionState, DominionError> {
        if hp < 0 {
            return Err(DominionError::InvalidState);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if state.tower != tower {
            return Err(DominionError::WrongTower);
        }
        if !state.siege.as_ref().is_some_and(|s| {
            s.active_at(now_seconds)
                && s.ownership_generation == state.ownership_generation
                && s.defender_expedition_id == state.owner_expedition_id
        }) {
            return Err(DominionError::SiegeNotActive);
        }
        if state.tower_destroyed && hp != 0 {
            return Err(DominionError::InvalidState);
        }
        if state.tower_hp == Some(hp) && (hp != 0 || state.tower_destroyed) {
            return Ok(state);
        }
        let revision = next_revision(&inner)?;
        state.tower_hp = Some(hp);
        // HP notification precedes the generic death callback. A save in between must
        // already preserve the destroyed, engravable state of the retained lodestone.
        state.tower_destroyed = hp == 0;
        if let Some(siege) = state.siege.as_mut() {
            siege.status = SiegeStatus::Active;
        }
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn reset_tower_destroyed(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
    ) -> Result<DominionState, DominionError> {
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if state.tower != tower {
            return Err(DominionError::WrongTower);
        }
        if !state.tower_destroyed {
            return Ok(state);
        }
        let revision = next_revision(&inner)?;
        state.tower_destroyed = false;
        state.tower_hp = None;
        inner.channels.retain(|(zone, _), _| *zone != zone_group_id);
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn begin_engraving(
        &self,
        zone_group_id: u32,
        tower: TowerIdentity,
        character_id: u32,
        expedition_id: u32,
        now_ms: i64,
    ) -> Result<EngravingHandle, DominionError> {
        if now_ms < 0 {
            return Err(DominionError::InvalidSchedule);
        }
        let mut inner = self.lock();
        let state = &inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state;
        engraving_allowed(state, tower, character_id, expedition_id, now_ms / 1000)?;
        let siege = state.siege.as_ref().ok_or(DominionError::SiegeNotActive)?;
        let ready_at_ms = now_ms
            .checked_add(ENGRAVING_MIN_DURATION_MS)
            .ok_or(DominionError::Overflow)?;
        if ready_at_ms >= siege.ends_at * 1000 {
            return Err(DominionError::TooLate);
        }
        if inner.channels.contains_key(&(zone_group_id, character_id)) {
            return Err(DominionError::AlreadyEngraving);
        }
        let channel_nonce = inner
            .channel_nonce
            .checked_add(1)
            .ok_or(DominionError::Overflow)?;
        let handle = EngravingHandle {
            zone_group_id,
            siege_id: siege.siege_id,
            character_id,
            tower,
            started_at_ms: now_ms,
            ready_at_ms,
            ownership_generation: state.ownership_generation,
            session_nonce: self.session_nonce,
            channel_nonce,
        };
        inner.channel_nonce = channel_nonce;
        inner
            .channels
            .insert((zone_group_id, character_id), handle.clone());
        Ok(handle)
    }

    pub fn abort_engraving(&self, handle: &EngravingHandle) -> bool {
        let mut inner = self.lock();
        let key = (handle.zone_group_id, handle.character_id);
        if inner.channels.get(&key) != Some(handle) {
            return false;
        }
        inner.channels.remove(&key);
        true
    }

    pub fn abort_character(&self, character_id: u32) {
        self.lock()
            .channels
            .retain(|(_, id), _| *id != character_id);
    }

    /// The caller must also establish that the same live SkillRef completed uninterrupted.
    pub fn complete_engraving(
        &self,
        handle: &EngravingHandle,
        current_expedition_id: u32,
        now_ms: i64,
    ) -> Result<DominionState, DominionError> {
        let mut inner = self.lock();
        if handle.session_nonce != self.session_nonce
            || inner
                .channels
                .get(&(handle.zone_group_id, handle.character_id))
                != Some(handle)
        {
            return Err(DominionError::StaleHandle);
        }
        let mut state = inner
            .states
            .get(&handle.zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        engraving_allowed(
            &state,
            handle.tower,
            handle.character_id,
            current_expedition_id,
            now_ms / 1000,
        )?;
        let siege = state.siege.as_mut().ok_or(DominionError::SiegeNotActive)?;
        if siege.siege_id != handle.siege_id
            || state.ownership_generation != handle.ownership_generation
        {
            return Err(DominionError::StaleHandle);
        }
        if now_ms < handle.ready_at_ms {
            return Err(DominionError::NotReady);
        }
        let generation = state
            .ownership_generation
            .checked_add(1)
            .ok_or(DominionError::Overflow)?;
        let revision = next_revision(&inner)?;
        state.owner_expedition_id = siege.attacker_expedition_id;
        state.ownership_generation = generation;
        state.reign_start_at = now_ms / 1000;
        state.last_siege_end_at = now_ms / 1000;
        state.tower_destroyed = false;
        state.tower_hp = None;
        siege.status = SiegeStatus::Finished;
        siege.outcome = SiegeOutcome::AttackerCaptured;
        siege.ended_at = now_ms / 1000;
        inner
            .channels
            .retain(|(zone, _), _| *zone != handle.zone_group_id);
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    /// Explicit schedule transitions only. An expired channel always leaves the defender
    /// in control; neither restart nor a elapsed deadline creates an attacker victory.
    pub fn tick(&self, now_seconds: i64) -> Result<Vec<DominionState>, DominionError> {
        if now_seconds < 0 {
            return Err(DominionError::InvalidSchedule);
        }
        let mut inner = self.lock();
        let mut changes = Vec::new();
        for entry in inner.states.values() {
            let mut state = entry.state.clone();
            let Some(siege) = state.siege.as_mut() else {
                continue;
            };
            if siege.status == SiegeStatus::Finished {
                continue;
            }
            if now_seconds >= siege.ends_at {
                siege.status = SiegeStatus::Finished;
                siege.outcome = SiegeOutcome::DefenderHeld;
                siege.ended_at = siege.ends_at;
                state.last_siege_end_at = siege.ends_at;
                state.tower_destroyed = false;
                state.tower_hp = None;
                changes.push(state);
            } else if now_seconds >= siege.starts_at && siege.status == SiegeStatus::Scheduled {
                siege.status = SiegeStatus::Active;
                changes.push(state);
            }
        }
        inner
            .revision
            .checked_add(changes.len() as u64)
            .ok_or(DominionError::Overflow)?;
        for state in &changes {
            if state
                .siege
                .as_ref()
                .is_some_and(|s| s.status == SiegeStatus::Finished)
            {
                inner
                    .channels
                    .retain(|(zone, _), _| *zone != state.zone_group_id);
            }
            let revision = inner.revision + 1;
            replace(&mut inner, state.clone(), revision);
        }
        Ok(changes)
    }

    pub fn cancel_siege(
        &self,
        zone_group_id: u32,
        now_seconds: i64,
    ) -> Result<DominionState, DominionError> {
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        let siege = state.siege.as_mut().ok_or(DominionError::SiegeNotActive)?;
        if siege.status == SiegeStatus::Finished {
            return Err(DominionError::SiegeNotActive);
        }
        if now_seconds < siege.scheduled_at {
            return Err(DominionError::InvalidSchedule);
        }
        let revision = next_revision(&inner)?;
        siege.status = SiegeStatus::Finished;
        siege.outcome = SiegeOutcome::Cancelled;
        siege.ended_at = now_seconds;
        state.last_siege_end_at = now_seconds;
        state.tower_destroyed = false;
        state.tower_hp = None;
        inner.channels.retain(|(zone, _), _| *zone != zone_group_id);
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn set_tax_rate(
        &self,
        zone_group_id: u32,
        owner_expedition_id: u32,
        tax_rate: i32,
        now_seconds: i64,
    ) -> Result<DominionState, DominionError> {
        if !(0..=50).contains(&tax_rate) || now_seconds < 0 {
            return Err(DominionError::InvalidTreasury);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if owner_expedition_id == 0 || state.owner_expedition_id != owner_expedition_id {
            return Err(DominionError::WrongOwner);
        }
        if state.tax_rate == tax_rate {
            return Ok(state);
        }
        if state.last_tax_rate_changed_at > 0
            && now_seconds.saturating_sub(state.last_tax_rate_changed_at)
                < TAX_RATE_CHANGE_COOLDOWN_SECONDS
        {
            return Err(DominionError::TaxRateChangeLater);
        }
        let revision = next_revision(&inner)?;
        state.tax_rate = tax_rate;
        state.last_tax_rate_changed_at = now_seconds;
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    /// Only real collected revenue should reach this entry point; no fabricated allowances.
    pub fn credit_treasury(
        &self,
        zone_group_id: u32,
        credit: Treasury,
    ) -> Result<DominionState, DominionError> {
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        let treasury = state
            .treasury
            .checked_add(credit)
            .ok_or(DominionError::InvalidTreasury)?;
        if treasury == state.treasury {
            return Ok(state);
        }
        let revision = next_revision(&inner)?;
        state.treasury = treasury;
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    /// Preflight the exact current ownership/rate and ledger capacity before charging. The
    /// callback follows authority -> economy lock order and must not call this manager.
    pub fn credit_property_tax_with(
        &self,
        expected: &DominionState,
        amount: i32,
        charge: impl FnOnce() -> bool,
    ) -> Result<DominionState, DominionError> {
        if amount < 0 {
            return Err(DominionError::InvalidTreasury);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&expected.zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if state.owner_expedition_id == 0
            || state.tower != expected.tower
            || state.owner_expedition_id != expected.owner_expedition_id
            || state.ownership_generation != expected.ownership_generation
            || state.tax_rate != expected.tax_rate
        {
            return Err(DominionError::StaleTaxQuote);
        }
        let treasury = state
            .treasury
            .checked_add(Treasury {
                cur_house_tax_money: amount,
                ..Treasury::default()
            })
            .ok_or(DominionError::InvalidTreasury)?;
        let revision = (amount != 0).then(|| next_revision(&inner)).transpose()?;
        if !charge() {
            return Err(DominionError::Resources);
        }
        if let Some(revision) = revision {
            state.treasury = treasury;
            replace(&mut inner, state.clone(), revision);
        }
        Ok(state)
    }

    /// Collect only actual property revenue. The independently accumulated Peace Fund is
    /// reserved for the siege victor and is never cleared by an ordinary collection.
    pub fn collect_property_tax_with(
        &self,
        expected: &DominionState,
        now_seconds: i64,
        pay: impl FnOnce(i32, i64) -> bool,
    ) -> Result<DominionState, DominionError> {
        if now_seconds < 0 {
            return Err(DominionError::InvalidSchedule);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&expected.zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if state.owner_expedition_id == 0
            || state.tower != expected.tower
            || state.owner_expedition_id != expected.owner_expedition_id
            || state.ownership_generation != expected.ownership_generation
        {
            return Err(DominionError::WrongOwner);
        }
        let amount = state.treasury.cur_house_tax_money;
        if amount == 0 {
            return Ok(state);
        }
        if now_seconds < state.last_paid_at {
            return Err(DominionError::InvalidSchedule);
        }
        let revision = next_revision(&inner)?;
        if !pay(amount, state.last_paid_at.max(state.reign_start_at)) {
            return Err(DominionError::Resources);
        }
        state.treasury.cur_house_tax_money = 0;
        state.last_paid_at = now_seconds;
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn collect_treasury_with(
        &self,
        zone_group_id: u32,
        owner_expedition_id: u32,
        now_seconds: i64,
        pay: impl FnOnce(Treasury) -> bool,
    ) -> Result<DominionState, DominionError> {
        if now_seconds < 0 {
            return Err(DominionError::InvalidSchedule);
        }
        let mut inner = self.lock();
        let mut state = inner
            .states
            .get(&zone_group_id)
            .ok_or(DominionError::Missing)?
            .state
            .clone();
        if owner_expedition_id == 0 || state.owner_expedition_id != owner_expedition_id {
            return Err(DominionError::WrongOwner);
        }
        let revision = next_revision(&inner)?;
        if !pay(state.treasury) {
            return Err(DominionError::Resources);
        }
        state.treasury = Treasury::default();
        state.last_paid_at = now_seconds;
        replace(&mut inner, state.clone(), revision);
        Ok(state)
    }

    pub fn prepare_save(&self) -> SaveBatch {
        let inner = self.lock();
        SaveBatch {
            session_nonce: self.session_nonce,
            rows: inner
                .states
                .values()
                .filter(|e| e.dirty)
                .map(|e| SavedRow {
                    revision: e.revision,
                    state: e.state.clone(),
                })
                .collect(),
            deleted: inner
                .removed
                .iter()
                .map(|(zone, revision)| (*zone, *revision))
                .collect(),
        }
    }

    /// Call only after this exact batch's transaction committed. Revision comparisons leave
    /// changes made while SQL was in flight dirty, including deletion/recreation of a zone.
    pub fn acknowledge_committed(&self, batch: &SaveBatch) {
        if batch.session_nonce != self.session_nonce {
            return;
        }
        let mut inner = self.lock();
        for row in &batch.rows {
            if let Some(entry) = inner.states.get_mut(&row.state.zone_group_id) {
                if entry.revision == row.revision {
                    entry.dirty = false;
                }
            }
        }
        for (zone, revision) in &batch.deleted {
            if inner.removed.get(zone) == Some(revision) {
                inner.removed.remove(zone);
            }
        }
    }

    /// SaveManager serializes world saves. Failed writes discard this transaction's pending
    /// acknowledgment while dirty rows remain available to the next retry.
    pub async fn save(&self, tx: &mut sqlx::Transaction<'_, sqlx::MySql>) -> sqlx::Result<usize> {
        let batch = self.prepare_save();
        self.save_prepared(tx, batch).await
    }

    /// Housing and Dominion snapshots are captured together under housing.operation_lock,
    /// released before awaiting SQL. Keep this exact revision receipt until transaction commit.
    pub async fn save_prepared(
        &self,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        batch: SaveBatch,
    ) -> sqlx::Result<usize> {
        self.lock().pending_save = None;
        let count = super::db::write_batch(tx, &batch).await?;
        self.lock().pending_save = Some(batch);
        Ok(count)
    }

    /// Conventional world-save callback; call immediately after tx.commit succeeds.
    pub fn commit_save(&self) {
        let batch = self.lock().pending_save.take();
        if let Some(batch) = batch {
            self.acknowledge_committed(&batch);
        }
    }
}
