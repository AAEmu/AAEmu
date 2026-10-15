//! The ranking boards at runtime: the global score store, the client requests, the recording
//! entry point the gameplay hooks call and the reward payout of a closed period.
//!
//! [`ranking_runtime`](super::ranking_runtime) owns the score rules and the pure views,
//! [`ranking_data`](super::ranking_data) the authored boards and
//! [`ranking_persistence`](super::ranking_persistence) the SQL; this module wires them to the
//! server.
//!
//! # Store
//!
//! The standings of a board span every character that ever scored, online or not, so the scores
//! live here, keyed by character, instead of on the (online-only) `Character`. The whole store is
//! loaded once at startup and upserted by every `SaveManager` pass.
//!
//! # Requests
//!
//! * `CSRankCharacterPacket` (0x12f, empty body) -> [`RankingManager::personal_packet`]: the
//!   requester's own row on every supported board (a board without a score still answers a row
//!   of zeros, which is what makes the client ask for its snapshot).
//! * `CSRankSnapshotPacket` (0x130, one `u32` board id) -> [`RankingManager::snapshot_packet`]:
//!   that board's standings. An unknown or racing board is not answered (a non-zero `error`
//!   makes the client drop the packet anyway).
//!
//! # Recording
//!
//! [`RankingManager::record`] is the only door gameplay uses: the arena result (instant game
//! end) and a landed trophy fish (`DoodadFuncConvertFish`, the fishing bridge). Every supported
//! board is offered the event; [`RankingState::record`] decides by kind, zone group and window.
//!
//! # Period close
//!
//! A score's period is closed once its `period_end` has passed. [`RankingManager::pay_closed`]
//! (run once a minute) pays each closed, unpaid period exactly once: the whole standings of the
//! period are walked, the `rank_scope_links` band a place falls into names the `rank_rewards`
//! row, and the reward item is mailed to the character as a `SYS_EXPRESS` system letter (offline
//! characters get it too: the item stays loose until the mailbox is read). An online winner is
//! told with `SCRankRewardMailPacket`. The period is recorded in `rank_period_payouts` before the
//! first letter is built, inside the economy operation gate the save snapshot is also captured
//! under.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex, MutexGuard};

use chrono::{DateTime, Utc};
use futures::future::BoxFuture;

use aaemu_commons::PacketStream;

use super::ranking_data::{RankDefinition, RankReward};
use super::ranking_packets::{
    CSRankSnapshotRequest, SCRankCharacterPacket, SCRankRewardMailPacket, SCRankSnapshotPacket,
    CS_RANK_SNAPSHOT_PACKET_OPCODE,
};
use super::ranking_persistence;
use super::ranking_runtime::{
    build_personal_rows, build_snapshot, ranked_scores, RankEvent, RankHolder, RankScore,
    RankingState, RecordOutcome,
};
use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::server::GameServer;
use crate::systems::economy::inventory::ItemTxn;
use crate::systems::economy::mail::{self, mail_type};
use crate::world::PlayerRef;

/// How often the closed periods are looked for.
const PAYOUT_INTERVAL_SECS: u64 = 60;

/// The reward letter's sender (plain 1.2 text, like the achievement letter: a locale-table
/// sender would show as NIL where the client has no entry).
pub const REWARD_MAIL_SENDER: &str = "Ranking Rewards";

/// The reward letter's title.
pub const REWARD_MAIL_TITLE: &str = "Ranking Reward";

/// The reward letter's body.
pub fn reward_mail_body(board: &RankDefinition, place: usize) -> String {
    format!("Reward for place {place} on the {} ranking.", board.name)
}

#[derive(Debug, Default)]
struct Store {
    /// `character_id -> that character's scores`.
    states: BTreeMap<u32, RankingState>,
    /// `(rank_id, period_start) -> paid_at` of every paid period.
    paid: BTreeMap<(u32, i64), i64>,
}

/// The global ranking score store (`server.systems.social.ranking`).
#[derive(Debug, Default)]
pub struct RankingManager {
    store: Mutex<Store>,
}

/// One closed, unpaid period handed to the payout.
struct ClosedPeriod {
    rank_id: u32,
    period_start: i64,
    scores: Vec<RankScore>,
}

/// One letter the payout built and sent, for the notification.
struct PaidLetter {
    rank_id: u32,
    character_id: u32,
}

impl RankingManager {
    fn store(&self) -> MutexGuard<'_, Store> {
        self.store.lock().expect("ranking store poisoned")
    }

    /// Whether no character has a score.
    pub fn is_empty(&self) -> bool {
        self.store().states.values().all(RankingState::is_empty)
    }

    /// Loads both tables (startup).
    pub async fn load(&self, pool: &sqlx::MySqlPool) -> Result<()> {
        let loaded = ranking_persistence::load(pool).await?;
        let mut store = self.store();
        store.states = loaded.states;
        store.paid = loaded
            .paid
            .into_iter()
            .map(|(rank_id, period_start, paid_at)| ((rank_id, period_start), paid_at))
            .collect();
        Ok(())
    }

    /// Upserts every score and payout (inside the `SaveManager` transaction). Returns the number
    /// of rows written.
    pub async fn save(&self, tx: &mut sqlx::Transaction<'_, sqlx::MySql>) -> Result<usize> {
        let (scores, paid) = {
            let store = self.store();
            let scores: Vec<RankScore> = store
                .states
                .values()
                .flat_map(|state| state.scores().cloned())
                .collect();
            let paid: Vec<(u32, i64, i64)> = store
                .paid
                .iter()
                .map(|((rank_id, period_start), paid_at)| (*rank_id, *period_start, *paid_at))
                .collect();
            (scores, paid)
        };
        ranking_persistence::write(tx, &scores, &paid).await
    }

    /// The stored score of a character on a board in a period.
    pub fn score_of(
        &self,
        character_id: u32,
        rank_id: u32,
        period_start: i64,
    ) -> Option<RankScore> {
        self.store()
            .states
            .get(&character_id)?
            .score(rank_id, period_start)
            .cloned()
    }

    /// Whether the rewards of a period were already paid.
    pub fn is_paid(&self, rank_id: u32, period_start: i64) -> bool {
        self.store().paid.contains_key(&(rank_id, period_start))
    }

    fn all_scores(&self) -> Vec<RankScore> {
        self.store()
            .states
            .values()
            .flat_map(|state| state.scores().cloned())
            .collect()
    }

    // -----------------------------------------------------------------------------------------
    // Recording
    // -----------------------------------------------------------------------------------------

    /// Offers one event to every supported board and returns how many stored scores moved.
    ///
    /// `zone_group_id` is the zone group the event happened in (boards are bound to one).
    pub fn record(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        event: RankEvent,
        zone_group_id: u32,
        now: DateTime<Utc>,
    ) -> usize {
        let holder = holder_of(server, player);
        let boards = &server.game_data.systems.social.ranking;
        let mut store = self.store();
        let state = store.states.entry(holder.character_id).or_default();
        let mut changed = 0;
        for board in boards.supported_ranks() {
            let outcome = state.record(board, &holder, event, zone_group_id, now);
            if outcome == RecordOutcome::Changed {
                changed += 1;
                tracing::debug!(
                    rank_id = board.id,
                    character_id = holder.character_id,
                    ?event,
                    "ranking score recorded"
                );
            }
        }
        changed
    }

    // -----------------------------------------------------------------------------------------
    // Requests
    // -----------------------------------------------------------------------------------------

    /// The `SCRankCharacterPacket` answering `CSRankCharacterPacket`: the character's own row on
    /// every supported board.
    ///
    /// `type_id` is `0`: the request carries no value to echo and the client keys the rows by
    /// their own `rank_id`.
    pub fn personal_packet(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        now: DateTime<Utc>,
    ) -> SCRankCharacterPacket {
        let holder = holder_of(server, player);
        let state = self
            .store()
            .states
            .get(&holder.character_id)
            .cloned()
            .unwrap_or_default();
        let boards = &server.game_data.systems.social.ranking;
        SCRankCharacterPacket {
            type_id: 0,
            error: 0,
            ack_time: u64::try_from(now.timestamp()).unwrap_or(0),
            rows: build_personal_rows(boards.supported_ranks(), &state, &holder, now),
        }
    }

    /// The `SCRankSnapshotPacket` answering `CSRankSnapshotPacket` for `rank_id`, `None` for a
    /// board this server does not run.
    pub fn snapshot_packet(
        &self,
        server: &GameServer,
        rank_id: u32,
        now: DateTime<Utc>,
    ) -> Option<SCRankSnapshotPacket> {
        let board = server.game_data.systems.social.ranking.rank(rank_id)?;
        build_snapshot(board, self.all_scores().iter(), now)
    }

    // -----------------------------------------------------------------------------------------
    // Period close
    // -----------------------------------------------------------------------------------------

    /// The closed, unpaid periods of the supported boards at `now`, with their scores.
    fn closed_periods(&self, server: &GameServer, now: DateTime<Utc>) -> Vec<ClosedPeriod> {
        let boards = &server.game_data.systems.social.ranking;
        let store = self.store();
        let mut periods: BTreeMap<(u32, i64), Vec<RankScore>> = BTreeMap::new();
        for score in store.states.values().flat_map(RankingState::scores) {
            if score.period_end > now.timestamp()
                || store
                    .paid
                    .contains_key(&(score.rank_id, score.period_start))
                || !boards.rank(score.rank_id).is_some_and(|b| b.is_supported())
            {
                continue;
            }
            periods
                .entry((score.rank_id, score.period_start))
                .or_default()
                .push(score.clone());
        }
        periods
            .into_iter()
            .map(|((rank_id, period_start), scores)| ClosedPeriod {
                rank_id,
                period_start,
                scores,
            })
            .collect()
    }

    /// Pays every closed, unpaid period once and returns how many letters were sent.
    ///
    /// Each period is claimed (recorded as paid) first, so a failing letter is skipped rather
    /// than retried into a duplicate for the winners that already got theirs.
    pub fn pay_closed(&self, server: &GameServer, now: DateTime<Utc>) -> usize {
        let mut letters = Vec::new();
        for period in self.closed_periods(server, now) {
            let Some(board) = server.game_data.systems.social.ranking.rank(period.rank_id) else {
                continue;
            };
            let mut txn = ItemTxn::begin(server);
            self.store()
                .paid
                .insert((period.rank_id, period.period_start), now.timestamp());
            let ranked = ranked_scores(board, period.period_start, period.scores.iter());
            for (index, score) in ranked.into_iter().enumerate() {
                let place = index + 1;
                let Some(reward) = reward_for_place(server, board, place) else {
                    continue;
                };
                if send_reward(&mut txn, board, score.character_id, place, reward) {
                    letters.push(PaidLetter {
                        rank_id: board.id,
                        character_id: score.character_id,
                    });
                }
            }
            drop(txn);
        }
        // The notice goes out after the operation gate is released.
        for letter in &letters {
            if let Some(player) = server.world.character_by_id(letter.character_id) {
                player.send(SCRankRewardMailPacket {
                    rank_id: letter.rank_id,
                });
            }
        }
        letters.len()
    }

    /// Spawns the once-a-minute payout check. The first tick fires at once, which pays the
    /// periods that closed while the server was down.
    pub fn start_tick(server: &Arc<GameServer>) {
        let weak = Arc::downgrade(server);
        tokio::spawn(async move {
            let mut iv =
                tokio::time::interval(std::time::Duration::from_secs(PAYOUT_INTERVAL_SECS));
            loop {
                iv.tick().await;
                let Some(server) = weak.upgrade() else {
                    break;
                };
                let paid = server
                    .systems
                    .social
                    .ranking
                    .pay_closed(&server, Utc::now());
                if paid > 0 {
                    tracing::info!("Ranking: mailed {paid} period rewards");
                }
            }
        });
    }
}

/// Who a score is stored and reported under. The game database does not know the login account
/// names, so the row carries the character name where the client shows the account.
fn holder_of(server: &GameServer, player: &PlayerRef) -> RankHolder {
    let c = player.data();
    RankHolder {
        account_id: c.account_id,
        account: c.unit.name.clone(),
        character_id: player.character_id,
        world_id: server.config.id,
    }
}

/// The reward the place falls into: the first `rank_scope_links` band holding it that has a
/// reward. Only an item reward is paid (the shipped table authors no appellation).
fn reward_for_place<'a>(
    server: &'a GameServer,
    board: &RankDefinition,
    place: usize,
) -> Option<&'a RankReward> {
    let place = i32::try_from(place).ok()?;
    server
        .game_data
        .systems
        .social
        .ranking
        .scope_rewards_of(board.id)
        .into_iter()
        .find(|(scope, _)| scope.scope_from <= place && place <= scope.scope_to)
        .and_then(|(_, reward)| reward)
}

/// Builds the reward item and mails it to `character_id`. `false` when the item or the letter
/// could not be made (logged).
fn send_reward(
    txn: &mut ItemTxn<'_>,
    board: &RankDefinition,
    character_id: u32,
    place: usize,
    reward: &RankReward,
) -> bool {
    let server = txn.server;
    if reward.appellation_id != 0 {
        tracing::warn!(
            reward_id = reward.id,
            appellation_id = reward.appellation_id,
            "ranking reward appellations are not granted"
        );
    }
    if reward.item_id == 0 || reward.item_count <= 0 {
        return false;
    }
    let grade = u8::try_from(reward.item_grade_id).unwrap_or(0);
    let Some(item) = server.items.create(
        reward.item_id,
        reward.item_count,
        grade,
        server.config.id,
        &server.game_data.items,
        &server.ids.item,
        Utc::now().timestamp(),
    ) else {
        tracing::warn!(
            reward_id = reward.id,
            item_id = reward.item_id,
            "ranking reward item has no template; not mailed"
        );
        return false;
    };
    let sent = mail::send_system_mail(
        txn,
        mail_type::SYS_EXPRESS,
        REWARD_MAIL_SENDER,
        character_id,
        REWARD_MAIL_TITLE,
        &reward_mail_body(board, place),
        0,
        vec![item],
    );
    if !sent {
        tracing::warn!(
            character_id,
            rank_id = board.id,
            "ranking reward letter could not be sent"
        );
    }
    sent
}

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// `CSRankSnapshotPacket` (0x130): the board the client has open.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRankSnapshotPacket(pub CSRankSnapshotRequest);

impl ClientPacket for CSRankSnapshotPacket {
    const NAME: &'static str = "CSRankSnapshotPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self(CSRankSnapshotRequest::read(s)?))
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(player) = &conn.active_char else {
                return Ok(());
            };
            match server
                .systems
                .social
                .ranking
                .snapshot_packet(server, self.0.rank_id, Utc::now())
            {
                Some(packet) => player.send(packet),
                None => tracing::debug!(rank_id = self.0.rank_id, "unknown ranking board"),
            }
            Ok(())
        })
    }
}

/// Registers the request the 1.2 opcode tables do not name.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSRankSnapshotPacket>(LEVEL_PLAIN, CS_RANK_SNAPSHOT_PACKET_OPCODE);
}

/// `CSRankCharacterPacket` (0x12f, empty body): answers with the requester's own rows.
pub fn handle_rank_character(server: &GameServer, conn: &GameConnection) {
    let Some(player) = &conn.active_char else {
        return;
    };
    player.send(
        server
            .systems
            .social
            .ranking
            .personal_packet(server, player, Utc::now()),
    );
}
