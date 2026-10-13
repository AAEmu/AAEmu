//! Bounty escrow and settlement state: the pure, in-memory ledger behind the wanted-player
//! bounty board.
//!
//! This module owns four things and nothing else:
//!
//! * [`BountyRules`] — the configurable policy: the placement fee (`fee_basis_points`, `0` by
//!   default) and the pledge lifetime (`expiry_seconds`, 24 hours by default). The selected
//!   defaults charge no fee and pay out when the wanted player is defeated.
//! * [`BountyLedger`] — the ledger of individual pledges and their settlement jobs. Every
//!   pledge keeps its payer and target identity, its escrowed copper, its creation and expiry
//!   instants, a stable id and its status.
//! * [`BountyLedger::prepare_placement`] / [`BountyLedger::commit_placement`] — the two-phase
//!   placement that lets the caller debit the wallet between prepare and commit.
//! * [`BountyLedger::settle_defeat`] / [`BountyLedger::expire_due`] /
//!   [`BountyLedger::mark_delivered`] — the settlement transitions that turn a pledge into a
//!   pending payout or refund job and record its delivery.
//!
//! Nothing here knows about packets, MySQL, the wallet, mail, timers or the clock: the caller
//! captures Unix UTC seconds and hands them in, and the manager that moves the copper lives
//! elsewhere. The whole state machine is therefore deterministic and testable without a server.
//!
//! # Units and identities
//!
//! Amounts are copper (`i64`), strictly positive at placement; the fee is charged **in
//! addition** to the escrow, so the wallet debit is `amount + fee`, never a deduction from the
//! pledge. Every arithmetic step that can leave the `i64` range is checked and answered with
//! [`BountyError::ArithmeticOverflow`] instead of wrapping, and the display total saturates
//! rather than wrapping.
//!
//! Identities are [`BountyActor`] (`character_id` + `account_id`). A placement and a payout
//! both refuse the same character and the same account: a character cannot put a bounty on
//! itself, and a killer on the target's own account cannot collect. The *wanted eligibility*
//! (the infamy / buff snapshot) and the killer's general eligibility are the caller's canonical
//! data and are passed in; this ledger invents no faction, party or level restriction.
//!
//! # Time
//!
//! Every instant is Unix UTC seconds captured by the caller. A pledge is **active** while
//! `now < expires` and **expired** once `now >= expires`: a defeat at the exact expiry instant
//! no longer pays, and the expiry sweep at that instant refunds. [`BountyLedger::active_bounty`]
//! reports the same half-open rule, so the display and the settlement agree at every instant.
//!
//! # Caller contracts
//!
//! **Placement** runs under the economy operation gate and captures `now` once:
//!
//! 1. [`BountyLedger::prepare_placement`] validates the request and reports the full debit
//!    (`amount + fee`). It does not change the ledger.
//! 2. The caller debits the payer's wallet by that debit.
//! 3. Without awaiting — still under the same gate and with the same captured `now` — the
//!    caller calls [`BountyLedger::commit_placement`]. If it answers
//!    [`BountyError::StalePlan`], the ledger changed since the prepare (another placement,
//!    settlement or restore); the caller must roll the debit back and retry. A commit never
//!    overwrites concurrent changes.
//!
//! **Defeat** passes the actual wanted-before-arrest flag and the already-vetted killer; only
//! unexpired active pledges of that exact target identity settle, and they leave the displayed
//! active total immediately. **Expiry** schedules one refund job per due pledge, back to the
//! original payer.
//!
//! **Delivery** runs under the same economy operation gate and has no standalone wallet commit:
//! the manager moves the copper in memory (wallet or mail), calls
//! [`BountyLedger::mark_delivered`] to record the receipt, and only then captures the ledger
//! snapshot together with the wallet/mail state as one financial snapshot. The outer database
//! write persists both halves atomically. If that write fails, the in-memory state stays dirty —
//! the copper already moved and the receipt is already marked — and the retry persists the same
//! snapshot without moving the copper again; `mark_delivered` is idempotent, so a replayed call
//! never credits twice.
//!
//! # What is deliberately not here
//!
//! The configuration binding (`serde`), the SQL tables, the wallet/mail delivery, the expiry
//! timer, the packet handlers and their registration, and any batching of jobs into one
//! aggregated payment are later, separate tasks. A pledge is its own stable settlement key; the
//! later manager may batch the jobs, but the ledger itself never merges them.

use std::collections::BTreeMap;
use std::fmt;

// ---- rules ----

/// The configurable placement policy.
///
/// `fee_basis_points` is the fee in hundredths of a percent of the pledge (`500` = 5%), charged
/// in addition to the escrow and rounded up to the next copper so a non-zero rate never
/// vanishes. `expiry_seconds` is the pledge lifetime. The defaults are the selected policy: no
/// fee and a 24-hour lifetime.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BountyRules {
    pub fee_basis_points: u32,
    pub expiry_seconds: i64,
}

impl Default for BountyRules {
    fn default() -> Self {
        Self {
            fee_basis_points: 0,
            expiry_seconds: 86_400,
        }
    }
}

impl BountyRules {
    /// The highest accepted fee: 100%, so a fee can never exceed the pledge it is charged on.
    pub const MAX_FEE_BASIS_POINTS: u32 = 10_000;

    /// Whether the rules can be used: a rate in `0..=10000` and a positive lifetime.
    pub fn validate(&self) -> Result<(), BountyError> {
        if self.fee_basis_points > Self::MAX_FEE_BASIS_POINTS {
            return Err(BountyError::InvalidFeeRate(self.fee_basis_points));
        }
        if self.expiry_seconds <= 0 {
            return Err(BountyError::InvalidExpiry(self.expiry_seconds));
        }
        Ok(())
    }

    /// The fee for a strictly positive pledge amount, rounded up to the next copper.
    ///
    /// The product is computed in `i128`, so no input can make it wrap; the result always fits
    /// back into `i64` because the rate is at most 100%.
    pub fn fee_for(&self, amount: i64) -> Result<i64, BountyError> {
        if amount <= 0 {
            return Err(BountyError::InvalidAmount(amount));
        }
        if self.fee_basis_points > Self::MAX_FEE_BASIS_POINTS {
            return Err(BountyError::InvalidFeeRate(self.fee_basis_points));
        }
        let fee = (i128::from(amount) * i128::from(self.fee_basis_points) + 9_999) / 10_000;
        i64::try_from(fee).map_err(|_| BountyError::ArithmeticOverflow("fee"))
    }

    /// The full wallet debit of a placement: the escrow plus the fee, checked.
    pub fn debit_for(&self, amount: i64) -> Result<i64, BountyError> {
        let fee = self.fee_for(amount)?;
        amount
            .checked_add(fee)
            .ok_or(BountyError::ArithmeticOverflow("debit"))
    }

    /// The expiry instant of a pledge created at `created`, checked.
    pub fn expires_at(&self, created: i64) -> Result<i64, BountyError> {
        if self.expiry_seconds <= 0 {
            return Err(BountyError::InvalidExpiry(self.expiry_seconds));
        }
        created
            .checked_add(self.expiry_seconds)
            .ok_or(BountyError::ArithmeticOverflow("expiry"))
    }
}

// ---- identities and rows ----

/// The canonical identity of a payer, target or recipient.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct BountyActor {
    pub character_id: u32,
    pub account_id: u32,
}

/// Whether an actor identity is real: both ids are nonzero (id 0 is never allocated).
fn valid_actor(actor: BountyActor) -> bool {
    actor.character_id != 0 && actor.account_id != 0
}

/// Where a pledge stands.
///
/// `PendingPayout` and `PendingRefund` each mean the pledge left the displayed active total and
/// its settlement job is not delivered yet; `Paid` means that job was delivered.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PledgeStatus {
    Active,
    PendingPayout,
    PendingRefund,
    Paid,
}

/// What a settlement job moves.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BountyJobKind {
    /// The escrow goes to the killer that defeated the wanted target.
    Payout,
    /// The escrow goes back to the pledge's original payer after expiry.
    Refund,
}

/// One individual pledge.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BountyPledge {
    pub id: u64,
    pub payer: BountyActor,
    pub target: BountyActor,
    /// Escrowed copper, strictly positive.
    pub amount: i64,
    /// Placement fee charged in addition to `amount`; not part of the escrow.
    pub fee: i64,
    /// Unix UTC seconds the pledge was created at.
    pub created: i64,
    /// Unix UTC seconds the pledge expires at; the pledge is active while `now < expires`.
    pub expires: i64,
    pub status: PledgeStatus,
}

/// One pending or delivered settlement of exactly one pledge.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BountyJob {
    pub id: u64,
    pub kind: BountyJobKind,
    pub pledge_id: u64,
    /// The killer for a payout, the original payer for a refund.
    pub recipient: BountyActor,
    /// The escrow moved; a fee is never part of a job.
    pub amount: i64,
    /// Unix UTC seconds the job was created at.
    pub created: i64,
    /// Set only by [`BountyLedger::mark_delivered`], once the in-memory wallet/mail move is done
    /// and before the financial snapshot is captured.
    pub delivered: bool,
}

/// A validated, not-yet-committed placement.
///
/// Only [`BountyLedger::prepare_placement`] can build one: every field is private and read
/// through its accessor, so a validated plan cannot be tampered with before the commit. It
/// reports the full wallet debit the caller has to charge.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PreparedPlacement {
    generation: u64,
    payer: BountyActor,
    target: BountyActor,
    amount: i64,
    fee: i64,
    debit: i64,
    now: i64,
    expires: i64,
}

impl PreparedPlacement {
    /// The payer whose wallet the debit is charged to.
    pub fn payer(&self) -> BountyActor {
        self.payer
    }

    /// The wanted target the pledge is placed on.
    pub fn target(&self) -> BountyActor {
        self.target
    }

    /// The escrow amount.
    pub fn amount(&self) -> i64 {
        self.amount
    }

    /// The placement fee charged in addition to the escrow.
    pub fn fee(&self) -> i64 {
        self.fee
    }

    /// The full wallet debit the caller charges: `amount + fee`.
    pub fn debit(&self) -> i64 {
        self.debit
    }

    /// The caller's captured instant, also the committed pledge's `created`.
    pub fn now(&self) -> i64 {
        self.now
    }

    /// The committed pledge's expiry instant.
    pub fn expires(&self) -> i64 {
        self.expires
    }
}

/// The displayed state of one target's active bounties.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ActiveBounty {
    /// The sum of the unexpired active pledges' escrow, saturating instead of wrapping.
    pub total: i64,
    /// The earliest expiry among them, if any.
    pub earliest_expiry: Option<i64>,
}

/// A whole-ledger snapshot for persistence, in ascending id order.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct BountySnapshot {
    pub pledges: Vec<BountyPledge>,
    pub jobs: Vec<BountyJob>,
}

// ---- errors ----

/// A refused bounty transition or an unusable saved row.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum BountyError {
    /// The placement amount is not strictly positive.
    InvalidAmount(i64),
    /// The fee rate is above [`BountyRules::MAX_FEE_BASIS_POINTS`].
    InvalidFeeRate(u32),
    /// The pledge lifetime is not positive.
    InvalidExpiry(i64),
    /// An actor identity has a zero character or account id.
    InvalidActor(&'static str),
    /// Payer and target are the same character (placement) or killer and target are (payout).
    SameCharacter,
    /// Payer and target share an account (placement) or killer and target do (payout).
    SameAccount,
    /// The caller's canonical wanted eligibility says the target is not wanted.
    TargetNotWanted,
    /// A checked amount, debit, expiry, id or generation would leave its range.
    ArithmeticOverflow(&'static str),
    /// The ledger changed between prepare and commit; the caller must roll the debit back.
    StalePlan,
    /// No settlement job has this id.
    UnknownJob(u64),
    /// A saved row is invalid or inconsistent with the rest of the snapshot.
    CorruptRow(String),
}

impl fmt::Display for BountyError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidAmount(amount) => {
                write!(f, "bounty amount must be positive, got {amount}")
            }
            Self::InvalidFeeRate(rate) => write!(
                f,
                "bounty fee rate {rate} basis points is above {}",
                BountyRules::MAX_FEE_BASIS_POINTS
            ),
            Self::InvalidExpiry(seconds) => {
                write!(f, "bounty expiry {seconds} seconds is not positive")
            }
            Self::InvalidActor(role) => {
                write!(f, "bounty {role} identity has a zero id")
            }
            Self::SameCharacter => {
                write!(f, "bounty payer and target are the same character")
            }
            Self::SameAccount => {
                write!(f, "bounty payer and target share an account")
            }
            Self::TargetNotWanted => write!(f, "bounty target is not wanted"),
            Self::ArithmeticOverflow(field) => write!(f, "bounty {field} overflowed"),
            Self::StalePlan => write!(f, "bounty placement plan is stale"),
            Self::UnknownJob(id) => write!(f, "unknown bounty job {id}"),
            Self::CorruptRow(reason) => write!(f, "corrupt bounty row: {reason}"),
        }
    }
}

impl std::error::Error for BountyError {}

// ---- settlement outcome ----

/// What [`BountyLedger::settle_defeat`] did.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DefeatOutcome {
    /// The caller's wanted-before-arrest flag was false: nothing settles.
    TargetNotWanted,
    /// The killer is the target or on the target's account: no payout is created.
    IneligibleKiller,
    /// No unexpired active pledge targets that identity (none, or all expired or already
    /// settling). A repeated defeat callback lands here and never creates a second payout.
    NoActivePledges,
    /// The settled pledges, each now `PendingPayout`, and their created jobs in ascending id
    /// order.
    Settled { jobs: Vec<BountyJob> },
}

// ---- ledger ----

/// The bounty ledger: every pledge and every settlement job, all transitions, no I/O.
///
/// The ledger is the single writer of pledge statuses and job receipts, and every mutating
/// transition bumps [`BountyLedger::generation`] so an outstanding [`PreparedPlacement`] can be
/// detected as stale. Ids are allocated monotonically from 1 and restored snapshots continue
/// above their highest id, so a saved receipt's key is never reused.
#[derive(Debug, Clone)]
pub struct BountyLedger {
    rules: BountyRules,
    generation: u64,
    next_pledge_id: u64,
    next_job_id: u64,
    pledges: BTreeMap<u64, BountyPledge>,
    jobs: BTreeMap<u64, BountyJob>,
}

impl Default for BountyLedger {
    fn default() -> Self {
        Self {
            rules: BountyRules::default(),
            generation: 0,
            next_pledge_id: 1,
            next_job_id: 1,
            pledges: BTreeMap::new(),
            jobs: BTreeMap::new(),
        }
    }
}

impl BountyLedger {
    /// A ledger with the given rules, refused when the rules are unusable.
    pub fn new(rules: BountyRules) -> Result<Self, BountyError> {
        rules.validate()?;
        Ok(Self {
            rules,
            ..Self::default()
        })
    }

    /// The policy in force.
    pub fn rules(&self) -> &BountyRules {
        &self.rules
    }

    /// The change counter: bumped by every mutation, captured by a prepare and compared by the
    /// matching commit.
    pub fn generation(&self) -> u64 {
        self.generation
    }

    /// One pledge by id.
    pub fn pledge(&self, id: u64) -> Option<&BountyPledge> {
        self.pledges.get(&id)
    }

    /// Every pledge, ascending id.
    pub fn pledges(&self) -> impl Iterator<Item = &BountyPledge> {
        self.pledges.values()
    }

    /// One settlement job by id.
    pub fn job(&self, id: u64) -> Option<&BountyJob> {
        self.jobs.get(&id)
    }

    /// Every settlement job, ascending id.
    pub fn jobs(&self) -> impl Iterator<Item = &BountyJob> {
        self.jobs.values()
    }

    /// Every job whose copper has not moved yet, ascending id.
    pub fn pending_jobs(&self) -> impl Iterator<Item = &BountyJob> {
        self.jobs.values().filter(|job| !job.delivered)
    }

    /// The displayed active total and earliest expiry of one target at `now`.
    ///
    /// Only pledges that are still `Active` and unexpired (`now < expires`) count, so a settled
    /// or expired pledge disappears from the display even before its job is delivered.
    pub fn active_bounty(&self, target_character_id: u32, now: i64) -> ActiveBounty {
        let mut total = 0i64;
        let mut earliest_expiry: Option<i64> = None;
        for pledge in self.pledges.values() {
            if pledge.status != PledgeStatus::Active
                || pledge.target.character_id != target_character_id
                || pledge.expires <= now
            {
                continue;
            }
            total = total.saturating_add(pledge.amount);
            earliest_expiry = Some(match earliest_expiry {
                Some(earliest) => earliest.min(pledge.expires),
                None => pledge.expires,
            });
        }
        ActiveBounty {
            total,
            earliest_expiry,
        }
    }

    // ---- placement ----

    /// Validates a placement and reports the full wallet debit without changing the ledger.
    ///
    /// `target_wanted` is the caller's canonical wanted eligibility of the target and `now` the
    /// caller's captured instant. The caller then debits [`PreparedPlacement::debit`] and calls
    /// [`Self::commit_placement`] under the same economy operation gate, without awaiting and
    /// with the same captured `now`.
    pub fn prepare_placement(
        &self,
        payer: BountyActor,
        target: BountyActor,
        amount: i64,
        target_wanted: bool,
        now: i64,
    ) -> Result<PreparedPlacement, BountyError> {
        if !valid_actor(payer) {
            return Err(BountyError::InvalidActor("payer"));
        }
        if !valid_actor(target) {
            return Err(BountyError::InvalidActor("target"));
        }
        if amount <= 0 {
            return Err(BountyError::InvalidAmount(amount));
        }
        if payer.character_id == target.character_id {
            return Err(BountyError::SameCharacter);
        }
        if payer.account_id == target.account_id {
            return Err(BountyError::SameAccount);
        }
        if !target_wanted {
            return Err(BountyError::TargetNotWanted);
        }
        let fee = self.rules.fee_for(amount)?;
        let debit = amount
            .checked_add(fee)
            .ok_or(BountyError::ArithmeticOverflow("debit"))?;
        let expires = self.rules.expires_at(now)?;
        Ok(PreparedPlacement {
            generation: self.generation,
            payer,
            target,
            amount,
            fee,
            debit,
            now,
            expires,
        })
    }

    /// Commits a prepared placement after the caller's external debit succeeded.
    ///
    /// Must be called under the same economy operation gate as the prepare, without awaiting in
    /// between. Answers [`BountyError::StalePlan`] when the ledger changed since the prepare —
    /// another placement, a settlement, a delivery or a restore — rather than overwriting that
    /// change; the caller rolls the debit back and retries.
    pub fn commit_placement(
        &mut self,
        plan: &PreparedPlacement,
    ) -> Result<BountyPledge, BountyError> {
        if plan.generation != self.generation {
            return Err(BountyError::StalePlan);
        }
        let next_pledge_id = self
            .next_pledge_id
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("pledge id"))?;
        let generation = self
            .generation
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("generation"))?;
        let pledge = BountyPledge {
            id: self.next_pledge_id,
            payer: plan.payer,
            target: plan.target,
            amount: plan.amount,
            fee: plan.fee,
            created: plan.now,
            expires: plan.expires,
            status: PledgeStatus::Active,
        };
        self.pledges.insert(pledge.id, pledge.clone());
        self.next_pledge_id = next_pledge_id;
        self.generation = generation;
        Ok(pledge)
    }

    // ---- settlement ----

    /// Settles the defeat of a wanted target into pending payout jobs.
    ///
    /// Only unexpired active pledges of the exact target identity settle, and only when
    /// `wanted_before_arrest` is true and the killer is neither the target nor on the target's
    /// account. Every settled pledge leaves the displayed active total immediately and becomes
    /// `PendingPayout` with one durable job; a repeated callback finds no active pledge and
    /// creates no second payout. The caller moves the copper for these jobs and receipts them
    /// with [`Self::mark_delivered`] under the economy operation gate (see the module docs).
    pub fn settle_defeat(
        &mut self,
        target: BountyActor,
        wanted_before_arrest: bool,
        killer: BountyActor,
        now: i64,
    ) -> Result<DefeatOutcome, BountyError> {
        if !valid_actor(target) {
            return Err(BountyError::InvalidActor("target"));
        }
        if !valid_actor(killer) {
            return Err(BountyError::InvalidActor("killer"));
        }
        if !wanted_before_arrest {
            return Ok(DefeatOutcome::TargetNotWanted);
        }
        if killer.character_id == target.character_id || killer.account_id == target.account_id {
            return Ok(DefeatOutcome::IneligibleKiller);
        }

        let settled: Vec<(u64, i64)> = self
            .pledges
            .values()
            .filter(|pledge| {
                pledge.status == PledgeStatus::Active
                    && pledge.target == target
                    && pledge.expires > now
            })
            .map(|pledge| (pledge.id, pledge.amount))
            .collect();
        if settled.is_empty() {
            return Ok(DefeatOutcome::NoActivePledges);
        }

        let next_job_id = self
            .next_job_id
            .checked_add(settled.len() as u64)
            .ok_or(BountyError::ArithmeticOverflow("job id"))?;
        let generation = self
            .generation
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("generation"))?;

        let mut jobs = Vec::with_capacity(settled.len());
        for (offset, (pledge_id, amount)) in settled.into_iter().enumerate() {
            let id = self.next_job_id + offset as u64;
            let pledge = self
                .pledges
                .get_mut(&pledge_id)
                .ok_or_else(|| BountyError::CorruptRow(format!("pledge {pledge_id} vanished")))?;
            pledge.status = PledgeStatus::PendingPayout;
            let job = BountyJob {
                id,
                kind: BountyJobKind::Payout,
                pledge_id,
                recipient: killer,
                amount,
                created: now,
                delivered: false,
            };
            self.jobs.insert(id, job.clone());
            jobs.push(job);
        }
        self.next_job_id = next_job_id;
        self.generation = generation;
        Ok(DefeatOutcome::Settled { jobs })
    }

    /// Schedules the refund jobs of every pledge expired at `now`, once each.
    ///
    /// An expired pledge that is still active becomes `PendingRefund` with one job back to its
    /// original payer, so the escrow is conserved. The fee charged at placement is not escrow
    /// and is not returned (the default policy charges none). A pledge already settling or paid
    /// is never touched, so calling this sweep again is a no-op.
    pub fn expire_due(&mut self, now: i64) -> Result<Vec<BountyJob>, BountyError> {
        let due: Vec<(u64, BountyActor, i64)> = self
            .pledges
            .values()
            .filter(|pledge| pledge.status == PledgeStatus::Active && pledge.expires <= now)
            .map(|pledge| (pledge.id, pledge.payer, pledge.amount))
            .collect();
        if due.is_empty() {
            return Ok(Vec::new());
        }

        let next_job_id = self
            .next_job_id
            .checked_add(due.len() as u64)
            .ok_or(BountyError::ArithmeticOverflow("job id"))?;
        let generation = self
            .generation
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("generation"))?;

        let mut jobs = Vec::with_capacity(due.len());
        for (offset, (pledge_id, payer, amount)) in due.into_iter().enumerate() {
            let id = self.next_job_id + offset as u64;
            let pledge = self
                .pledges
                .get_mut(&pledge_id)
                .ok_or_else(|| BountyError::CorruptRow(format!("pledge {pledge_id} vanished")))?;
            pledge.status = PledgeStatus::PendingRefund;
            let job = BountyJob {
                id,
                kind: BountyJobKind::Refund,
                pledge_id,
                recipient: payer,
                amount,
                created: now,
                delivered: false,
            };
            self.jobs.insert(id, job.clone());
            jobs.push(job);
        }
        self.next_job_id = next_job_id;
        self.generation = generation;
        Ok(jobs)
    }

    /// Records that a job's copper was delivered and its pledge is paid.
    ///
    /// Called under the economy operation gate after the wallet/mail move and **before** the
    /// financial snapshot that persists the ledger together with the wallet/mail state, so the
    /// outer database write commits the receipt and the copper as one. A failed write leaves the
    /// in-memory state dirty and the retry persists it unchanged — no copper is moved again.
    /// Answers `Ok(true)` on the first call and `Ok(false)` when the job was already delivered,
    /// so a replay can never credit twice.
    pub fn mark_delivered(&mut self, job_id: u64) -> Result<bool, BountyError> {
        let (pledge_id, kind, delivered) = match self.jobs.get(&job_id) {
            Some(job) => (job.pledge_id, job.kind, job.delivered),
            None => return Err(BountyError::UnknownJob(job_id)),
        };
        if delivered {
            return Ok(false);
        }
        let generation = self
            .generation
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("generation"))?;
        let expected = match kind {
            BountyJobKind::Payout => PledgeStatus::PendingPayout,
            BountyJobKind::Refund => PledgeStatus::PendingRefund,
        };
        let pledge = self.pledges.get_mut(&pledge_id).ok_or_else(|| {
            BountyError::CorruptRow(format!(
                "job {job_id} references missing pledge {pledge_id}"
            ))
        })?;
        if pledge.status != expected {
            return Err(BountyError::CorruptRow(format!(
                "job {job_id} does not match pledge {pledge_id} status"
            )));
        }
        let job = self
            .jobs
            .get_mut(&job_id)
            .ok_or(BountyError::UnknownJob(job_id))?;
        pledge.status = PledgeStatus::Paid;
        job.delivered = true;
        self.generation = generation;
        Ok(true)
    }

    // ---- persistence ----

    /// The whole ledger as save rows in ascending id order.
    ///
    /// The later manager captures this together with the wallet/mail state — after the delivery
    /// receipts are marked — and writes both halves in one database transaction, so the receipts
    /// and the copper commit or roll back together.
    pub fn snapshot(&self) -> BountySnapshot {
        BountySnapshot {
            pledges: self.pledges.values().cloned().collect(),
            jobs: self.jobs.values().cloned().collect(),
        }
    }

    /// Replaces the ledger with a restored snapshot, or refuses it whole.
    ///
    /// Every row is validated before anything is applied, so a corrupt snapshot leaves the
    /// ledger untouched and answers [`BountyError::CorruptRow`] instead of silently dropping a
    /// receipt or making a paid row payable again. A delivered job stays delivered and its
    /// pledge stays `Paid`; a pending job stays pending; ids continue above the restored ones;
    /// and any outstanding [`PreparedPlacement`] becomes stale.
    ///
    /// An empty ledger — the startup case — takes any valid snapshot. A live ledger only takes a
    /// snapshot that keeps or advances what it already has: an existing pledge or job cannot be
    /// dropped or altered in its immutable fields, a pledge cannot be demoted, and a delivered
    /// receipt cannot be un-delivered, so replaying an older snapshot can never revive a paid or
    /// pending settlement.
    pub fn restore(&mut self, snapshot: BountySnapshot) -> Result<(), BountyError> {
        let mut pledges = BTreeMap::new();
        for pledge in snapshot.pledges {
            validate_pledge_row(&pledge)?;
            let id = pledge.id;
            if pledges.insert(id, pledge).is_some() {
                return Err(BountyError::CorruptRow(format!("duplicate pledge id {id}")));
            }
        }

        let mut jobs = BTreeMap::new();
        let mut settled: BTreeMap<u64, u64> = BTreeMap::new();
        for job in snapshot.jobs {
            if job.id == 0 {
                return Err(BountyError::CorruptRow("job id 0 is not valid".into()));
            }
            if job.amount <= 0 {
                return Err(BountyError::CorruptRow(format!(
                    "job {} has amount {}",
                    job.id, job.amount
                )));
            }
            if !valid_actor(job.recipient) {
                return Err(BountyError::CorruptRow(format!(
                    "job {} has a zero recipient identity",
                    job.id
                )));
            }
            let pledge = pledges.get(&job.pledge_id).ok_or_else(|| {
                BountyError::CorruptRow(format!(
                    "job {} references missing pledge {}",
                    job.id, job.pledge_id
                ))
            })?;
            if job.amount != pledge.amount {
                return Err(BountyError::CorruptRow(format!(
                    "job {} moves {} but pledge {} holds {}",
                    job.id, job.amount, job.pledge_id, pledge.amount
                )));
            }
            match job.kind {
                BountyJobKind::Refund if job.recipient != pledge.payer => {
                    return Err(BountyError::CorruptRow(format!(
                        "refund job {} does not pay pledge {}'s payer",
                        job.id, job.pledge_id
                    )));
                }
                BountyJobKind::Payout
                    if job.recipient.character_id == pledge.target.character_id
                        || job.recipient.account_id == pledge.target.account_id =>
                {
                    return Err(BountyError::CorruptRow(format!(
                        "payout job {} pays the target of pledge {}",
                        job.id, job.pledge_id
                    )));
                }
                _ => {}
            }
            let consistent = match pledge.status {
                PledgeStatus::Active => false,
                PledgeStatus::PendingPayout => job.kind == BountyJobKind::Payout && !job.delivered,
                PledgeStatus::PendingRefund => job.kind == BountyJobKind::Refund && !job.delivered,
                PledgeStatus::Paid => job.delivered,
            };
            if !consistent {
                return Err(BountyError::CorruptRow(format!(
                    "job {} does not match pledge {} status",
                    job.id, job.pledge_id
                )));
            }
            let id = job.id;
            if jobs.insert(id, job).is_some() {
                return Err(BountyError::CorruptRow(format!("duplicate job id {id}")));
            }
            if settled.insert(pledge.id, id).is_some() {
                return Err(BountyError::CorruptRow(format!(
                    "pledge {} has more than one settlement job",
                    pledge.id
                )));
            }
        }
        for pledge in pledges.values() {
            if pledge.status != PledgeStatus::Active && !settled.contains_key(&pledge.id) {
                return Err(BountyError::CorruptRow(format!(
                    "pledge {} is {:?} without a settlement job",
                    pledge.id, pledge.status
                )));
            }
        }
        self.ensure_restore_is_monotonic(&pledges, &jobs)?;

        let next_pledge_id = pledges
            .keys()
            .next_back()
            .copied()
            .unwrap_or(0)
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("pledge id"))?;
        let next_job_id = jobs
            .keys()
            .next_back()
            .copied()
            .unwrap_or(0)
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("job id"))?;
        let generation = self
            .generation
            .checked_add(1)
            .ok_or(BountyError::ArithmeticOverflow("generation"))?;

        self.pledges = pledges;
        self.jobs = jobs;
        self.next_pledge_id = next_pledge_id;
        self.next_job_id = next_job_id;
        self.generation = generation;
        Ok(())
    }

    /// Refuses a restore that would lose or demote what the live ledger already recorded.
    ///
    /// An existing pledge or job cannot be dropped or altered in its immutable fields, a pledge
    /// status may only stay or advance (`Active` → pending → `Paid`, with the pending kind
    /// kept), and a delivered receipt cannot be un-delivered. New rows are free to appear, so a
    /// newer snapshot still applies; an older one that would revive a paid or pending
    /// settlement is answered with [`BountyError::CorruptRow`] and changes nothing.
    fn ensure_restore_is_monotonic(
        &self,
        pledges: &BTreeMap<u64, BountyPledge>,
        jobs: &BTreeMap<u64, BountyJob>,
    ) -> Result<(), BountyError> {
        for current in self.pledges.values() {
            let Some(restored) = pledges.get(&current.id) else {
                return Err(BountyError::CorruptRow(format!(
                    "restore would drop pledge {}",
                    current.id
                )));
            };
            if !immutable_pledge_fields_match(current, restored) {
                return Err(BountyError::CorruptRow(format!(
                    "restore would alter pledge {}",
                    current.id
                )));
            }
            if !pledge_status_advances(current.status, restored.status) {
                return Err(BountyError::CorruptRow(format!(
                    "restore would demote pledge {} from {:?} to {:?}",
                    current.id, current.status, restored.status
                )));
            }
        }
        for current in self.jobs.values() {
            let Some(restored) = jobs.get(&current.id) else {
                return Err(BountyError::CorruptRow(format!(
                    "restore would drop job {}",
                    current.id
                )));
            };
            if !immutable_job_fields_match(current, restored) {
                return Err(BountyError::CorruptRow(format!(
                    "restore would alter job {}",
                    current.id
                )));
            }
            if current.delivered && !restored.delivered {
                return Err(BountyError::CorruptRow(format!(
                    "restore would demote delivered job {}",
                    current.id
                )));
            }
        }
        Ok(())
    }
}

/// Whether two rows of the same pledge describe the same immutable pledge: identity, escrow,
/// fee and timestamps. The status is the only part a restore may move.
fn immutable_pledge_fields_match(a: &BountyPledge, b: &BountyPledge) -> bool {
    a.payer == b.payer
        && a.target == b.target
        && a.amount == b.amount
        && a.fee == b.fee
        && a.created == b.created
        && a.expires == b.expires
}

/// Whether two rows of the same job describe the same immutable settlement. Only `delivered`
/// may move, and only from false to true.
fn immutable_job_fields_match(a: &BountyJob, b: &BountyJob) -> bool {
    a.kind == b.kind
        && a.pledge_id == b.pledge_id
        && a.recipient == b.recipient
        && a.amount == b.amount
        && a.created == b.created
}

/// Whether `restored` is the same status or a later one in the settlement lifecycle:
/// `Active` → `PendingPayout`/`PendingRefund` (kind kept) → `Paid`.
fn pledge_status_advances(current: PledgeStatus, restored: PledgeStatus) -> bool {
    matches!(
        (current, restored),
        (PledgeStatus::Active, _)
            | (
                PledgeStatus::PendingPayout,
                PledgeStatus::PendingPayout | PledgeStatus::Paid
            )
            | (
                PledgeStatus::PendingRefund,
                PledgeStatus::PendingRefund | PledgeStatus::Paid
            )
            | (PledgeStatus::Paid, PledgeStatus::Paid)
    )
}

/// A saved pledge row must be a real pledge: a nonzero id, real payer and target identities
/// that are neither the same character nor on the same account, a positive escrow, a
/// non-negative fee and an expiry strictly after its creation.
fn validate_pledge_row(pledge: &BountyPledge) -> Result<(), BountyError> {
    if pledge.id == 0 {
        return Err(BountyError::CorruptRow("pledge id 0 is not valid".into()));
    }
    if !valid_actor(pledge.payer) {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} has a zero payer identity",
            pledge.id
        )));
    }
    if !valid_actor(pledge.target) {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} has a zero target identity",
            pledge.id
        )));
    }
    if pledge.payer.character_id == pledge.target.character_id {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} puts a bounty on its own character",
            pledge.id
        )));
    }
    if pledge.payer.account_id == pledge.target.account_id {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} puts a bounty on its own account",
            pledge.id
        )));
    }
    if pledge.amount <= 0 {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} has amount {}",
            pledge.id, pledge.amount
        )));
    }
    if pledge.fee < 0 {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} has negative fee {}",
            pledge.id, pledge.fee
        )));
    }
    if pledge.expires <= pledge.created {
        return Err(BountyError::CorruptRow(format!(
            "pledge {} expires at {} but was created at {}",
            pledge.id, pledge.expires, pledge.created
        )));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn actor(character_id: u32, account_id: u32) -> BountyActor {
        BountyActor {
            character_id,
            account_id,
        }
    }

    fn place(
        ledger: &mut BountyLedger,
        payer: BountyActor,
        target: BountyActor,
        amount: i64,
        now: i64,
    ) -> BountyPledge {
        let plan = ledger
            .prepare_placement(payer, target, amount, true, now)
            .expect("prepared placement");
        ledger.commit_placement(&plan).expect("committed placement")
    }

    #[test]
    fn defaults_charge_no_fee_and_expire_after_one_day() {
        let rules = BountyRules::default();
        assert_eq!(rules.fee_basis_points, 0);
        assert_eq!(rules.expiry_seconds, 86_400);
        assert_eq!(rules.validate(), Ok(()));

        let mut ledger = BountyLedger::default();
        assert_eq!(ledger.rules(), &rules);
        assert_eq!(ledger.generation(), 0);
        let pledge = place(&mut ledger, actor(1, 10), actor(2, 20), 500, 1_000);
        assert_eq!(pledge.id, 1);
        assert_eq!(pledge.fee, 0);
        assert_eq!(pledge.created, 1_000);
        assert_eq!(pledge.expires, 1_000 + 86_400);
        assert_eq!(pledge.status, PledgeStatus::Active);
        assert_eq!(ledger.generation(), 1);
        assert_eq!(ledger.active_bounty(2, 1_000).total, 500);
    }

    #[test]
    fn fee_rounds_up_and_debit_is_checked() {
        let rules = BountyRules {
            fee_basis_points: 500,
            expiry_seconds: 60,
        };
        assert_eq!(rules.fee_for(1_000).unwrap(), 50);
        assert_eq!(rules.debit_for(1_000).unwrap(), 1_050);

        // A rate that does not divide evenly rounds up so it never silently vanishes.
        let tiny = BountyRules {
            fee_basis_points: 1,
            expiry_seconds: 60,
        };
        assert_eq!(tiny.fee_for(1).unwrap(), 1);
        assert_eq!(tiny.fee_for(9_999).unwrap(), 1);
        assert_eq!(tiny.fee_for(10_000).unwrap(), 1);
        assert_eq!(tiny.fee_for(10_001).unwrap(), 2);

        // No rate, no fee, even at the ceiling; a non-positive amount is refused outright.
        assert_eq!(BountyRules::default().fee_for(i64::MAX).unwrap(), 0);
        assert_eq!(tiny.fee_for(0), Err(BountyError::InvalidAmount(0)));
        assert_eq!(tiny.fee_for(-5), Err(BountyError::InvalidAmount(-5)));

        // A plan reports the fee and the full debit it is charged on.
        let mut ledger = BountyLedger::new(rules).unwrap();
        let plan = ledger
            .prepare_placement(actor(1, 10), actor(2, 20), 1_000, true, 0)
            .unwrap();
        assert_eq!((plan.fee(), plan.debit()), (50, 1_050));
        let pledge = ledger.commit_placement(&plan).unwrap();
        assert_eq!((pledge.amount, pledge.fee), (1_000, 50));
    }

    #[test]
    fn rules_validation_and_placement_math_never_wrap() {
        assert_eq!(
            BountyRules {
                fee_basis_points: 10_001,
                expiry_seconds: 60,
            }
            .validate(),
            Err(BountyError::InvalidFeeRate(10_001))
        );
        assert_eq!(
            BountyRules {
                fee_basis_points: 0,
                expiry_seconds: 0,
            }
            .validate(),
            Err(BountyError::InvalidExpiry(0))
        );
        assert!(BountyLedger::new(BountyRules {
            fee_basis_points: 0,
            expiry_seconds: -1,
        })
        .is_err());

        // A fee on an amount near the ceiling overflows the debit instead of wrapping it.
        let ledger = BountyLedger::new(BountyRules {
            fee_basis_points: 1,
            expiry_seconds: 86_400,
        })
        .unwrap();
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 20), i64::MAX, true, 0),
            Err(BountyError::ArithmeticOverflow("debit"))
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 20), 1, true, i64::MAX),
            Err(BountyError::ArithmeticOverflow("expiry"))
        );

        // No fee: the amount is the full debit, and the display total saturates, not wraps.
        let mut ledger = BountyLedger::default();
        let plan = ledger
            .prepare_placement(actor(1, 10), actor(2, 20), i64::MAX, true, 0)
            .unwrap();
        assert_eq!((plan.fee(), plan.debit()), (0, i64::MAX));
        ledger.commit_placement(&plan).unwrap();
        place(&mut ledger, actor(3, 30), actor(2, 20), i64::MAX, 0);
        assert_eq!(ledger.active_bounty(2, 0).total, i64::MAX);
    }

    #[test]
    fn placement_checks_identity_wanted_eligibility_and_amount() {
        let ledger = BountyLedger::default();
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(1, 20), 100, true, 0),
            Err(BountyError::SameCharacter)
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 10), 100, true, 0),
            Err(BountyError::SameAccount)
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 20), 100, false, 0),
            Err(BountyError::TargetNotWanted)
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 20), 0, true, 0),
            Err(BountyError::InvalidAmount(0))
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 20), -5, true, 0),
            Err(BountyError::InvalidAmount(-5))
        );
        // Nothing was created by any refused request.
        assert_eq!(ledger.pledges().count(), 0);
        assert_eq!(ledger.generation(), 0);
    }

    #[test]
    fn topups_accumulate_and_keep_their_own_expiry() {
        let mut ledger = BountyLedger::default();
        let first = place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let second = place(&mut ledger, actor(3, 30), actor(2, 20), 500, 100);
        assert_eq!(first.expires, 86_400);
        assert_eq!(second.expires, 86_500);

        let view = ledger.active_bounty(2, 0);
        assert_eq!(view.total, 800);
        assert_eq!(view.earliest_expiry, Some(86_400));

        // Another target's pledge is not part of this total.
        place(&mut ledger, actor(1, 10), actor(9, 90), 700, 0);
        assert_eq!(ledger.active_bounty(2, 0).total, 800);
        assert_eq!(ledger.active_bounty(9, 0).total, 700);
        assert_eq!(ledger.active_bounty(7, 0), ActiveBounty::default());
    }

    #[test]
    fn expiry_boundary_is_half_open() {
        let mut ledger = BountyLedger::default();
        let pledge = place(&mut ledger, actor(1, 10), actor(2, 20), 300, 1_000);
        let expiry = pledge.expires;

        // Active up to, but not including, the expiry instant.
        assert_eq!(ledger.active_bounty(2, expiry - 1).total, 300);
        assert_eq!(
            ledger.active_bounty(2, expiry - 1).earliest_expiry,
            Some(expiry)
        );
        assert_eq!(ledger.active_bounty(2, expiry), ActiveBounty::default());

        // The sweep at the instant before expiry has nothing to do; at the instant itself it
        // schedules the payer's refund.
        assert!(ledger.expire_due(expiry - 1).unwrap().is_empty());
        assert_eq!(
            ledger.pledge(pledge.id).unwrap().status,
            PledgeStatus::Active
        );
        let refunds = ledger.expire_due(expiry).unwrap();
        assert_eq!(refunds.len(), 1);
        assert_eq!(refunds[0].kind, BountyJobKind::Refund);
        assert_eq!(refunds[0].recipient, actor(1, 10));
        assert_eq!(refunds[0].amount, 300);
        assert_eq!(refunds[0].created, expiry);
        assert_eq!(
            ledger.pledge(pledge.id).unwrap().status,
            PledgeStatus::PendingRefund
        );
    }

    #[test]
    fn defeat_pays_before_expiry_and_not_after() {
        let mut ledger = BountyLedger::default();
        let alive = place(&mut ledger, actor(1, 10), actor(2, 20), 300, 80_000);
        let expired = place(&mut ledger, actor(3, 30), actor(2, 20), 500, 1_000);
        let expiry = expired.expires;

        // At the expired pledge's boundary instant only the still-unexpired pledge settles.
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), expiry)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        assert_eq!(jobs.len(), 1);
        assert_eq!(jobs[0].pledge_id, alive.id);
        assert_eq!(jobs[0].kind, BountyJobKind::Payout);
        assert_eq!(jobs[0].recipient, actor(4, 40));
        assert_eq!(jobs[0].amount, 300);
        assert_eq!(jobs[0].created, expiry);
        assert_eq!(
            ledger.pledge(alive.id).unwrap().status,
            PledgeStatus::PendingPayout
        );

        // The expired pledge is not payable by a defeat; the expiry sweep refunds its payer.
        assert_eq!(
            ledger.pledge(expired.id).unwrap().status,
            PledgeStatus::Active
        );
        assert_eq!(ledger.active_bounty(2, expiry).total, 0);
        let refunds = ledger.expire_due(expiry).unwrap();
        assert_eq!(refunds.len(), 1);
        assert_eq!(refunds[0].pledge_id, expired.id);
        assert_eq!(refunds[0].recipient, actor(3, 30));
    }

    #[test]
    fn defeat_expiry_and_delivery_are_one_shot() {
        let mut ledger = BountyLedger::default();
        let pledge = place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 100)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        let job_id = jobs[0].id;

        // A repeated defeat callback finds nothing active and creates no second payout.
        assert_eq!(
            ledger
                .settle_defeat(actor(2, 20), true, actor(4, 40), 200)
                .unwrap(),
            DefeatOutcome::NoActivePledges
        );
        // A later expiry sweep does not schedule a refund for a pledge already settling.
        assert!(ledger.expire_due(1_000_000).unwrap().is_empty());
        assert_eq!(ledger.jobs().count(), 1);

        // Delivery marks the job and its pledge paid; repeating it is a no-op.
        assert!(ledger.mark_delivered(job_id).unwrap());
        assert!(!ledger.mark_delivered(job_id).unwrap());
        assert!(ledger.job(job_id).unwrap().delivered);
        assert_eq!(ledger.pledge(pledge.id).unwrap().status, PledgeStatus::Paid);
        assert!(ledger.pending_jobs().next().is_none());

        // The paid pledge is never payable again.
        assert_eq!(
            ledger
                .settle_defeat(actor(2, 20), true, actor(4, 40), 300_000)
                .unwrap(),
            DefeatOutcome::NoActivePledges
        );
    }

    #[test]
    fn a_failed_delivery_stays_pending_and_retries_the_same_job() {
        let mut ledger = BountyLedger::default();
        place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 0)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        let job_id = jobs[0].id;

        // The wallet transaction failed: the job stays pending and the pledge is already out of
        // the displayed active total.
        assert_eq!(ledger.pending_jobs().count(), 1);
        assert_eq!(ledger.active_bounty(2, 1).total, 0);

        // The retry delivers the same job, never a new one.
        assert!(ledger.mark_delivered(job_id).unwrap());
        assert_eq!(ledger.jobs().count(), 1);
        assert!(ledger.pending_jobs().next().is_none());
        assert_eq!(
            ledger.mark_delivered(999),
            Err(BountyError::UnknownJob(999))
        );
    }

    #[test]
    fn payout_identities_are_checked() {
        let mut ledger = BountyLedger::default();
        place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);

        // The target itself, and another character on the target's account, cannot collect.
        assert_eq!(
            ledger
                .settle_defeat(actor(2, 20), true, actor(2, 20), 0)
                .unwrap(),
            DefeatOutcome::IneligibleKiller
        );
        assert_eq!(
            ledger
                .settle_defeat(actor(2, 20), true, actor(5, 20), 0)
                .unwrap(),
            DefeatOutcome::IneligibleKiller
        );
        // The actual wanted-before-arrest flag gates the settlement.
        assert_eq!(
            ledger
                .settle_defeat(actor(2, 20), false, actor(4, 40), 0)
                .unwrap(),
            DefeatOutcome::TargetNotWanted
        );
        // Another wanted player's defeat does not touch this target's pledges.
        assert_eq!(
            ledger
                .settle_defeat(actor(7, 70), true, actor(4, 40), 0)
                .unwrap(),
            DefeatOutcome::NoActivePledges
        );
        // A pledge settles only for the exact target identity it was placed against.
        assert_eq!(
            ledger
                .settle_defeat(actor(2, 99), true, actor(4, 40), 0)
                .unwrap(),
            DefeatOutcome::NoActivePledges
        );
        assert_eq!(ledger.pledge(1).unwrap().status, PledgeStatus::Active);
    }

    #[test]
    fn snapshot_and_restore_preserve_pending_and_paid_receipts() {
        let mut ledger = BountyLedger::default();
        let first = place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let second = place(&mut ledger, actor(3, 30), actor(2, 20), 500, 10);
        let refunded = place(&mut ledger, actor(6, 60), actor(8, 80), 700, 0);

        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 100)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        assert_eq!(jobs.len(), 2);
        assert!(ledger.mark_delivered(jobs[0].id).unwrap());
        let refunds = ledger.expire_due(86_410).unwrap();
        assert_eq!(refunds.len(), 1);
        assert_eq!(refunds[0].pledge_id, refunded.id);
        assert_eq!(refunds[0].recipient, actor(6, 60));

        let snapshot = ledger.snapshot();
        assert!(snapshot.pledges.windows(2).all(|w| w[0].id < w[1].id));
        assert!(snapshot.jobs.windows(2).all(|w| w[0].id < w[1].id));

        let mut restored = BountyLedger::default();
        restored.restore(snapshot).unwrap();
        assert_eq!(
            restored.pledge(first.id).unwrap().status,
            PledgeStatus::Paid
        );
        assert_eq!(
            restored.pledge(second.id).unwrap().status,
            PledgeStatus::PendingPayout
        );
        assert_eq!(
            restored.pledge(refunded.id).unwrap().status,
            PledgeStatus::PendingRefund
        );
        assert!(restored.job(jobs[0].id).unwrap().delivered);
        assert_eq!(restored.pending_jobs().count(), 2);

        // A delivered receipt is never payable again after a replay.
        assert!(!restored.mark_delivered(jobs[0].id).unwrap());
        assert_eq!(
            restored
                .settle_defeat(actor(2, 20), true, actor(4, 40), 200)
                .unwrap(),
            DefeatOutcome::NoActivePledges
        );

        // Ids continue above the restored ones instead of reusing a receipt's key.
        let plan = restored
            .prepare_placement(actor(9, 90), actor(5, 50), 100, true, 200)
            .unwrap();
        let next = restored.commit_placement(&plan).unwrap();
        assert!(next.id > refunded.id);
        assert_eq!(restored.jobs().count(), 3);
    }

    #[test]
    fn restore_rejects_corrupt_rows_and_leaves_the_ledger_untouched() {
        let mut ledger = BountyLedger::default();
        place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 0)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        ledger.mark_delivered(jobs[0].id).unwrap();
        let good = ledger.snapshot();

        let mut refund_ledger = BountyLedger::default();
        place(&mut refund_ledger, actor(6, 60), actor(8, 80), 700, 0);
        refund_ledger.expire_due(86_400).unwrap();
        let refund_snapshot = refund_ledger.snapshot();

        // A paid pledge whose job row is missing must not come back payable.
        let mut missing_job = good.clone();
        missing_job.jobs.clear();
        // A pledge restored as active while its settlement job exists would be payable twice.
        let mut active_with_job = good.clone();
        active_with_job.pledges[0].status = PledgeStatus::Active;
        // A delivered job restored as pending would pay twice.
        let mut pending_receipt = good.clone();
        pending_receipt.jobs[0].delivered = false;
        // A payout whose recipient is the target itself.
        let mut self_payout = good.clone();
        self_payout.jobs[0].recipient = actor(2, 20);
        // A refund whose recipient is not the original payer.
        let mut wrong_refund = refund_snapshot.clone();
        wrong_refund.jobs[0].recipient = actor(9, 90);
        // Non-positive escrow, inverted expiry, duplicate ids and a dangling reference.
        let mut zero_amount = good.clone();
        zero_amount.pledges[0].amount = 0;
        let mut negative_fee = good.clone();
        negative_fee.pledges[0].fee = -1;
        let mut inverted_expiry = good.clone();
        inverted_expiry.pledges[0].expires = inverted_expiry.pledges[0].created;
        let mut duplicate = good.clone();
        duplicate.pledges.push(duplicate.pledges[0].clone());
        let mut dangling = good.clone();
        dangling.jobs[0].pledge_id = 999;

        for corrupt in [
            missing_job,
            active_with_job,
            pending_receipt,
            self_payout,
            wrong_refund,
            zero_amount,
            negative_fee,
            inverted_expiry,
            duplicate,
            dangling,
        ] {
            let mut target = BountyLedger::default();
            let before = target.snapshot();
            assert!(
                matches!(target.restore(corrupt), Err(BountyError::CorruptRow(_))),
                "expected a corrupt-row refusal"
            );
            assert_eq!(target.snapshot(), before);
            assert_eq!(target.generation(), 0);
        }

        // The intact snapshot still restores.
        let mut restored = BountyLedger::default();
        restored.restore(good).unwrap();
        assert_eq!(restored.pledge(1).unwrap().status, PledgeStatus::Paid);
    }

    #[test]
    fn an_abandoned_plan_leaves_no_pledge_and_a_stale_plan_is_rejected() {
        let mut ledger = BountyLedger::default();
        let plan = ledger
            .prepare_placement(actor(1, 10), actor(2, 20), 300, true, 0)
            .unwrap();
        assert_eq!((plan.fee(), plan.debit()), (0, 300));
        assert_eq!(plan.expires(), 86_400);
        assert_eq!(plan.now(), 0);

        // Abandoned: the caller's debit failed, so nothing was committed.
        assert_eq!(ledger.pledges().count(), 0);
        assert_eq!(ledger.generation(), 0);

        // A plan whose ledger changed in between is stale, so concurrent changes survive.
        let other = ledger
            .prepare_placement(actor(3, 30), actor(2, 20), 500, true, 0)
            .unwrap();
        let committed = ledger.commit_placement(&other).unwrap();
        assert_eq!(committed.id, 1);
        assert_eq!(ledger.commit_placement(&plan), Err(BountyError::StalePlan));
        assert_eq!(ledger.pledges().count(), 1);

        // A plan is one-shot even when nothing else changed in between.
        let one_shot = ledger
            .prepare_placement(actor(5, 50), actor(2, 20), 100, true, 0)
            .unwrap();
        ledger.commit_placement(&one_shot).unwrap();
        assert_eq!(
            ledger.commit_placement(&one_shot),
            Err(BountyError::StalePlan)
        );
        assert_eq!(ledger.pledges().count(), 2);
    }

    #[test]
    fn restore_invalidates_outstanding_plans() {
        let mut ledger = BountyLedger::default();
        let plan = ledger
            .prepare_placement(actor(1, 10), actor(2, 20), 300, true, 0)
            .unwrap();
        ledger.restore(BountySnapshot::default()).unwrap();
        assert_eq!(ledger.commit_placement(&plan), Err(BountyError::StalePlan));
    }

    #[test]
    fn refunds_conserve_escrow_and_never_return_the_fee() {
        let rules = BountyRules {
            fee_basis_points: 1_000,
            expiry_seconds: 3_600,
        };
        let mut ledger = BountyLedger::new(rules).unwrap();
        let plan = ledger
            .prepare_placement(actor(1, 10), actor(2, 20), 1_000, true, 0)
            .unwrap();
        assert_eq!((plan.fee(), plan.debit()), (100, 1_100));
        let pledge = ledger.commit_placement(&plan).unwrap();

        let refunds = ledger.expire_due(3_600).unwrap();
        assert_eq!(refunds.len(), 1);
        assert_eq!(refunds[0].amount, pledge.amount);
        assert_eq!(refunds[0].recipient, pledge.payer);
        assert_eq!(refunds[0].pledge_id, pledge.id);
    }

    #[test]
    fn views_are_sorted_and_do_not_mutate() {
        let mut ledger = BountyLedger::default();
        let first = place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let second = place(&mut ledger, actor(3, 30), actor(2, 20), 500, 10);
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 20)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };

        let pledge_ids: Vec<u64> = ledger.pledges().map(|pledge| pledge.id).collect();
        assert_eq!(pledge_ids, vec![first.id, second.id]);
        let job_ids: Vec<u64> = ledger.jobs().map(|job| job.id).collect();
        assert_eq!(job_ids, jobs.iter().map(|job| job.id).collect::<Vec<u64>>());
        let pending_ids: Vec<u64> = ledger.pending_jobs().map(|job| job.id).collect();
        assert_eq!(pending_ids, job_ids);

        // Restoring a snapshot in any order still yields ascending views.
        let mut reversed = ledger.snapshot();
        reversed.pledges.reverse();
        reversed.jobs.reverse();
        let mut restored = BountyLedger::default();
        restored.restore(reversed).unwrap();
        let restored_ids: Vec<u64> = restored.pledges().map(|pledge| pledge.id).collect();
        assert_eq!(restored_ids, vec![first.id, second.id]);
        assert_eq!(restored.snapshot(), ledger.snapshot());

        let before = ledger.snapshot();
        for _ in 0..2 {
            let _ = ledger.active_bounty(2, 0);
            let _ = ledger.pending_jobs().count();
            let _ = ledger.pledges().count();
            assert_eq!(ledger.snapshot(), before);
        }
    }

    #[test]
    fn a_prepared_plan_exposes_only_its_validated_values() {
        let mut ledger = BountyLedger::new(BountyRules {
            fee_basis_points: 500,
            expiry_seconds: 60,
        })
        .unwrap();
        let plan = ledger
            .prepare_placement(actor(1, 10), actor(2, 20), 1_000, true, 5_000)
            .unwrap();
        assert_eq!(plan.payer(), actor(1, 10));
        assert_eq!(plan.target(), actor(2, 20));
        assert_eq!(plan.amount(), 1_000);
        assert_eq!(plan.fee(), 50);
        assert_eq!(plan.debit(), 1_050);
        assert_eq!(plan.now(), 5_000);
        assert_eq!(plan.expires(), 5_060);

        // The commit stores exactly the validated plan, so the caller's debit and the pledge
        // agree.
        let pledge = ledger.commit_placement(&plan).unwrap();
        assert_eq!(pledge.payer, plan.payer());
        assert_eq!(pledge.target, plan.target());
        assert_eq!(pledge.amount, plan.amount());
        assert_eq!(pledge.fee, plan.fee());
        assert_eq!(pledge.created, plan.now());
        assert_eq!(pledge.expires, plan.expires());
    }

    #[test]
    fn identities_must_be_nonzero_and_distinct() {
        let ledger = BountyLedger::default();
        assert_eq!(
            ledger.prepare_placement(actor(0, 10), actor(2, 20), 100, true, 0),
            Err(BountyError::InvalidActor("payer"))
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 0), actor(2, 20), 100, true, 0),
            Err(BountyError::InvalidActor("payer"))
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(0, 20), 100, true, 0),
            Err(BountyError::InvalidActor("target"))
        );
        assert_eq!(
            ledger.prepare_placement(actor(1, 10), actor(2, 0), 100, true, 0),
            Err(BountyError::InvalidActor("target"))
        );
        assert_eq!(ledger.pledges().count(), 0);

        let mut ledger = BountyLedger::default();
        place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        assert_eq!(
            ledger.settle_defeat(actor(0, 20), true, actor(4, 40), 0),
            Err(BountyError::InvalidActor("target"))
        );
        assert_eq!(
            ledger.settle_defeat(actor(2, 20), true, actor(0, 40), 0),
            Err(BountyError::InvalidActor("killer"))
        );
        assert_eq!(ledger.pledge(1).unwrap().status, PledgeStatus::Active);

        // Restored rows must carry the same real, distinct identities.
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 0)
            .unwrap();
        let DefeatOutcome::Settled { .. } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        let good = ledger.snapshot();

        let mut zero_payer = good.clone();
        zero_payer.pledges[0].payer = actor(0, 10);
        let mut zero_target = good.clone();
        zero_target.pledges[0].target = actor(2, 0);
        let mut self_target = good.clone();
        self_target.pledges[0].target = actor(1, 10);
        let mut shared_account = good.clone();
        shared_account.pledges[0].target = actor(2, 10);
        let mut zero_recipient = good.clone();
        zero_recipient.jobs[0].recipient = actor(0, 40);
        for corrupt in [
            zero_payer,
            zero_target,
            self_target,
            shared_account,
            zero_recipient,
        ] {
            assert!(matches!(
                BountyLedger::default().restore(corrupt),
                Err(BountyError::CorruptRow(_))
            ));
        }
        BountyLedger::default().restore(good).unwrap();
    }

    #[test]
    fn restore_requires_job_amount_to_equal_the_pledge() {
        let mut ledger = BountyLedger::default();
        place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 0)
            .unwrap();
        let DefeatOutcome::Settled { .. } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        let good = ledger.snapshot();

        // A job that moves more or less than its pledge holds would mint or burn copper.
        let mut minted = good.clone();
        minted.jobs[0].amount = 301;
        let mut shaved = good.clone();
        shaved.jobs[0].amount = 299;
        for corrupt in [minted, shaved] {
            let mut target = BountyLedger::default();
            assert!(matches!(
                target.restore(corrupt),
                Err(BountyError::CorruptRow(_))
            ));
            assert_eq!(target.snapshot(), BountySnapshot::default());
        }
        BountyLedger::default().restore(good).unwrap();
    }

    #[test]
    fn restore_refuses_to_demote_or_drop_settlements() {
        let mut ledger = BountyLedger::default();
        place(&mut ledger, actor(1, 10), actor(2, 20), 300, 0);
        let outcome = ledger
            .settle_defeat(actor(2, 20), true, actor(4, 40), 0)
            .unwrap();
        let DefeatOutcome::Settled { jobs } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        assert!(ledger.mark_delivered(jobs[0].id).unwrap());
        let paid = ledger.snapshot();
        let before = ledger.snapshot();

        // An older snapshot that still shows the pledge pending and the job undelivered must
        // not revive the payout.
        let mut older = paid.clone();
        older.pledges[0].status = PledgeStatus::PendingPayout;
        older.jobs[0].delivered = false;
        assert!(matches!(
            ledger.restore(older),
            Err(BountyError::CorruptRow(_))
        ));
        assert_eq!(ledger.snapshot(), before);
        assert!(!ledger.mark_delivered(jobs[0].id).unwrap());

        // Dropping the paid receipt (the pledge, the job, or everything) and altering the paid
        // pledge's escrow are refused too.
        let mut missing_job = paid.clone();
        missing_job.jobs.clear();
        let mut missing_pledge = paid.clone();
        missing_pledge.pledges.clear();
        let mut altered = paid.clone();
        altered.pledges[0].amount = 400;
        altered.jobs[0].amount = 400;
        for corrupt in [
            missing_job,
            missing_pledge,
            BountySnapshot::default(),
            altered,
        ] {
            assert!(matches!(
                ledger.restore(corrupt),
                Err(BountyError::CorruptRow(_))
            ));
            assert_eq!(ledger.snapshot(), before);
        }

        // A pending settlement cannot be demoted to active and replayed either; advancing it
        // to paid through a newer snapshot is allowed.
        let mut pending = BountyLedger::default();
        place(&mut pending, actor(1, 10), actor(2, 20), 300, 0);
        let outcome = pending
            .settle_defeat(actor(2, 20), true, actor(4, 40), 0)
            .unwrap();
        let DefeatOutcome::Settled { .. } = outcome else {
            panic!("expected a settlement, got {outcome:?}")
        };
        let pending_before = pending.snapshot();
        let mut revived = pending_before.clone();
        revived.pledges[0].status = PledgeStatus::Active;
        revived.jobs.clear();
        assert!(matches!(
            pending.restore(revived),
            Err(BountyError::CorruptRow(_))
        ));
        assert_eq!(pending.snapshot(), pending_before);

        let mut newer = pending_before.clone();
        newer.pledges[0].status = PledgeStatus::Paid;
        newer.jobs[0].delivered = true;
        pending.restore(newer).unwrap();
        assert_eq!(pending.pledge(1).unwrap().status, PledgeStatus::Paid);
        assert!(!pending.mark_delivered(1).unwrap());
    }
}
