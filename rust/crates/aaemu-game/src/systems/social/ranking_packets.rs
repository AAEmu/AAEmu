//! Ranking board wire types of the 1.2 client (`SCRankCharacterPacket`, `SCRankSnapshotPacket`,
//! `SCRankRewardMailPacket`) plus the client's snapshot *request*, which the 1.2 opcode tables do
//! not name.
//!
//! The field order and widths below are not guesswork: they were recovered from the running 1.2
//! client's unpacked `x2game.dll` (image base `0x38FF0000`) and then confirmed live with the real
//! client against injected packets. The three `SC*` packets are plain "view" serializers: the
//! client reads each field in the order the serializer walks the packet object, so the wire body is
//! exactly the concatenation below (in-object padding is never transmitted).
//!
//! Evidence, per packet (addresses are client `.text` VAs):
//!
//! * `SCRankRewardMailPacket` (0x1f9): constructor `0x391b75b0` (object size `0x10`) → vtable
//!   `0x399b42f4` → serializer `0x397b89d0`, a single `type` u32.
//! * `SCRankCharacterPacket` (0x1fa): constructor `0x391b7630` (size `0x428`) → vtable
//!   `0x399b4300` → serializer `0x397c4820` (header) → `0x3961f360` (count) → `0x3961f1a0` (row).
//!   The row array is capped at 16 (the serializer clamps to `0x10`, the constant at `0x399f9a74`).
//! * `SCRankSnapshotPacket` (0x1fb): constructor `0x391b7700` (size `0x1488`) → vtable
//!   `0x399b430c` → serializer `0x397c48b0` (header) → `0x3961f3b0` (board) → `0x3961f2c0` (entry).
//!   Entries and scopes are capped at 100 (the `MAX_RANK_SNAPSHOT` constant the client publishes
//!   as `100.0`, `0x390765f3`). The excess is what closes the object size exactly:
//!   `0x18 + 8 + 8 + 8 + 4 + 4 + 100*0x28 + 100*0xC = 0x1488`, which is also why the board's first
//!   field (`board_id`) is an 8-byte primitive and not a 4-byte one.
//! * Stream primitive widths were calibrated on the live client: slot `0x38` = i64 unix seconds,
//!   `0x3c` = i32, `0x44` = u8, `0x48` = 8-byte (the board id), `0x4c` = i32, `0x50` = u16,
//!   `0x78` = bool (one byte), `0xd8`/`0xdc` = `u16`-length-prefixed UTF-8 string.
//!
//! Consumer semantics the writer must respect (client `0x39076980` for 0x1fa, `0x39076df0` for
//! 0x1fb, both reached from handlers `0x39182340` / `0x39182370`):
//!
//! * both packets are dropped when the 32-bit value at body offset 2 (`error` plus the untouched
//!   2 bytes after it) is non-zero, so `error` must be 0 for a payload to be applied;
//! * the 0x1fa rows are filed in the client's per-board map under their **second** type field
//!   (`rank_id`, object `+0x1C`), which is therefore the board key;
//! * 0x1fb only *updates* an existing map entry and is discarded when the outer `rank_id` is not
//!   already known — a 0x1fa answer for that board must be accepted first;
//! * the client resolves the displayed name from the first entry field (`character_id`) in its own
//!   name cache and the world name from `world_id`, so a line is only named when the client
//!   already knows that character.
//!
//! Scores are signed (`v1`/`v2` are read with `cvtsi2ss`, treated as `i32`), timestamps are unix
//! seconds. `SCRankAlarmPacket` (0x205) is already ported in `packets/g2c_misc.rs` and is not
//! repeated here.
//!
//! This module is wire-only: no manager, no registry wiring, no score state. It is not declared in
//! `systems/social/mod.rs` yet (the snapshot request has no handler until the board manager
//! exists), so it only compiles once that one `pub mod ranking_packets;` line is added.

use aaemu_commons::{Marshal, PacketStream};

use super::packets::sc_packet;
use crate::error::{GameError, Result};

// ---------------------------------------------------------------------------------------------
// Limits
// ---------------------------------------------------------------------------------------------

/// The most rows `SCRankCharacterPacket` carries: the client's record array holds `0x10` entries
/// and its reader clamps a larger count to that (serializer `0x3961f360`, constant `0x399f9a74`).
pub const MAX_CHARACTER_RANK_ROWS: usize = 16;

/// The most entries `SCRankSnapshotPacket` carries (the client's `MAX_RANK_SNAPSHOT` = 100).
pub const MAX_SNAPSHOT_ENTRIES: usize = 100;

/// The most scopes `SCRankSnapshotPacket` carries (the same 100-entry clamp as the entries).
pub const MAX_SNAPSHOT_SCOPES: usize = 100;

/// The client reads a row's account name into a fixed 16-byte buffer: the serializer passes
/// `0x10` as the maximum before the string accessor (`push 0x10` at `0x3961f1f5`).
pub const ACCOUNT_MAX_BYTES: usize = 16;

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

/// Writes a row's account name as the client's `u16`-length-prefixed UTF-8 string, cut to the
/// 16-byte buffer the client reads it into. The cut never splits a UTF-8 sequence: a multi-byte
/// character that would straddle the limit is dropped whole.
fn write_account(s: &mut PacketStream, account: &str) {
    let mut end = account.len().min(ACCOUNT_MAX_BYTES);
    while !account.is_char_boundary(end) {
        end -= 1;
    }
    s.write_str(&account[..end]);
}

/// One row of `SCRankCharacterPacket`: the character's standing on one board (the client's
/// 64-byte record, in its wire form — the object's padding is not transmitted).
#[derive(Debug, Clone, PartialEq)]
pub struct SCRankCharacterRow {
    /// The account the row belongs to (the client keeps it for the tooltip/name fallback).
    pub account_id: u32,
    /// Account name, at most [`ACCOUNT_MAX_BYTES`] bytes on the wire.
    pub account: String,
    /// The character the row is about (the client's own rows; its record is filed under
    /// [`Self::rank_id`], not under this id).
    pub character_id: u32,
    /// The board this row belongs to — the client's per-board map key (record `+0x1C`).
    pub rank_id: u32,
    pub world_id: u8,
    /// Primary score (signed; the client converts it to a number with `cvtsi2ss`).
    pub v1: i32,
    /// `isV1Sum`: one byte — whether [`Self::v1`] is a running total rather than a best value
    /// (the two fishing boards differ exactly in this: best length vs summed weight).
    pub sum_v1: bool,
    /// Secondary score (signed).
    pub v2: i32,
    /// `isV2Sum`, one byte.
    pub sum_v2: bool,
    /// When the score was recorded, unix seconds.
    pub recorded: u64,
}

impl Marshal for SCRankCharacterRow {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.account_id);
        write_account(s, &self.account);
        s.write_u32(self.character_id)
            .write_u32(self.rank_id)
            .write_u8(self.world_id)
            .write_i32(self.v1)
            .write_bool(self.sum_v1)
            .write_i32(self.v2)
            .write_bool(self.sum_v2)
            .write_u64(self.recorded);
    }
}

sc_packet!(
    /// `SCRankCharacterPacket` (0x1fa): the requester's own rows, one per board, answering
    /// `CSRankCharacterPacket` (0x12f, empty body).
    SCRankCharacterPacket {
        /// The header type the client sends/is answered with; its meaning is not proven (the
        /// live tests echoed the board id), so the name stays generic.
        type_id: u32,
        /// Zero for an applied payload; the client drops the packet when this (as a 32-bit read)
        /// is non-zero.
        error: u16,
        /// When the rows were assembled, unix seconds.
        ack_time: u64,
        /// At most [`MAX_CHARACTER_RANK_ROWS`] rows are written; a longer list is truncated so the
        /// count always matches the bytes that follow.
        rows: Vec<SCRankCharacterRow>,
    } |self, s| {
        s.write_u32(self.type_id)
            .write_u16(self.error)
            .write_u64(self.ack_time)
            .write_u32(self.rows.len().min(MAX_CHARACTER_RANK_ROWS) as u32);
        for row in self.rows.iter().take(MAX_CHARACTER_RANK_ROWS) {
            row.write(s);
        }
    }
);

/// One line of a snapshot board (`SCRankSnapshotPacket`).
#[derive(Debug, Clone, PartialEq)]
pub struct SCRankSnapshotEntry {
    /// The holder, resolved to a name by the client's own name cache.
    pub character_id: u32,
    /// The board the line belongs to; the client does not read it back, it only has to agree with
    /// the key the 0x1fa rows were filed under.
    pub rank_id: u32,
    pub world_id: u8,
    /// Primary score (signed).
    pub v1: i32,
    /// Secondary score (signed).
    pub v2: i32,
    /// When the score was recorded, unix seconds.
    pub updated: u64,
    /// The place on the board, best first.
    pub ranking: u32,
}

impl Marshal for SCRankSnapshotEntry {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_u32(self.rank_id)
            .write_u8(self.world_id)
            .write_i32(self.v1)
            .write_i32(self.v2)
            .write_u64(self.updated)
            .write_u32(self.ranking);
    }
}

/// One scope (placing band) total of a board. The band's name and range come from the client's own
/// `rank_scopes` table; only the id and the two figures travel.
#[derive(Debug, Clone, PartialEq)]
pub struct SCRankSnapshotScope {
    /// The band id the client matches against its local `rank_scopes` rows.
    pub scope_id: u32,
    pub v1: i32,
    pub v2: i32,
}

impl Marshal for SCRankSnapshotScope {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.scope_id)
            .write_i32(self.v1)
            .write_i32(self.v2);
    }
}

sc_packet!(
    /// `SCRankSnapshotPacket` (0x1fb): one board's snapshot, answering the client's snapshot
    /// request ([`CSRankSnapshotRequest`]).
    SCRankSnapshotPacket {
        /// The requested board (`ranks.id`). The client only applies the packet when its per-board
        /// map already holds this key, i.e. after an accepted `SCRankCharacterPacket` row for it.
        rank_id: u32,
        /// Zero for an applied payload; see `SCRankCharacterPacket::error`.
        error: u16,
        /// Opaque 64-bit board id (client stream slot `0x48`). The client copies it into its cache
        /// but no getter reads it, so the value is free; the width must be 8 bytes.
        board_id: i64,
        /// When the snapshot was taken, unix seconds.
        snapshot_updated: u64,
        /// When the board resets, unix seconds.
        reset_updated: u64,
        /// At most [`MAX_SNAPSHOT_ENTRIES`] entries are written.
        entries: Vec<SCRankSnapshotEntry>,
        /// At most [`MAX_SNAPSHOT_SCOPES`] scopes are written.
        scopes: Vec<SCRankSnapshotScope>,
    } |self, s| {
        s.write_u32(self.rank_id)
            .write_u16(self.error)
            .write_i64(self.board_id)
            .write_u64(self.snapshot_updated)
            .write_u64(self.reset_updated)
            .write_u32(self.entries.len().min(MAX_SNAPSHOT_ENTRIES) as u32);
        for entry in self.entries.iter().take(MAX_SNAPSHOT_ENTRIES) {
            entry.write(s);
        }
        s.write_u32(self.scopes.len().min(MAX_SNAPSHOT_SCOPES) as u32);
        for scope in self.scopes.iter().take(MAX_SNAPSHOT_SCOPES) {
            scope.write(s);
        }
    }
);

sc_packet!(
    /// `SCRankRewardMailPacket` (0x1f9): a single board id, the client's "a rank reward mail
    /// arrived" notice.
    SCRankRewardMailPacket {
        /// The board the reward belongs to.
        rank_id: u32,
    } |self, s| {
        s.write_u32(self.rank_id);
    }
);

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// `CSRankSnapshotPacket`: the client asks for the board it has open.
///
/// The 1.2 opcode tables do not name this opcode (`opcodes::cs` jumps 0x12f → 0x131), so the
/// number is declared here with its evidence:
///
/// * the client's request writer is `0x397c79f0` (class vtable `0x399a12b8`), which serializes a
///   `rankType` variant carrying one u32 `type` — the variant selector is not written, so the body
///   is the u32 alone;
/// * the send site is the `X2Rank.RequestRankSnapshots` binding `0x39075070`, which fills the u32
///   with the selected board key and sends it under opcode `0x130`;
/// * live capture of the real client confirms the body is exactly `01 00 00 00` for board 1.
pub const CS_RANK_SNAPSHOT_PACKET_OPCODE: u16 = 0x130;

/// The body of [`CS_RANK_SNAPSHOT_PACKET_OPCODE`]: one board id, nothing else.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRankSnapshotRequest {
    /// `ranks.id` of the board the client has selected.
    pub rank_id: u32,
}

impl CSRankSnapshotRequest {
    /// The exact body length the client writes.
    pub const BODY_LEN: usize = 4;

    /// Decodes a complete request body, the shape the tests (and the eventual handler) use.
    ///
    /// The client writes exactly [`Self::BODY_LEN`] bytes, so a shorter or longer body is rejected
    /// rather than silently truncated or padded.
    pub fn decode(body: &[u8]) -> Result<Self> {
        if body.len() != Self::BODY_LEN {
            return Err(GameError::Protocol(format!(
                "CSRankSnapshotPacket body is {} bytes, expected {}",
                body.len(),
                Self::BODY_LEN
            )));
        }
        let mut s = PacketStream::from_bytes(body);
        Self::read(&mut s)
    }

    /// Reads the body from a packet stream (what a `ClientPacket` for opcode
    /// [`CS_RANK_SNAPSHOT_PACKET_OPCODE`] will call once the board manager exists).
    pub fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            rank_id: s.read_u32()?,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::{PacketDef, LEVEL_PLAIN};
    use crate::opcodes::sc;

    fn body(p: &impl Marshal) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write(&mut s);
        s.into_bytes()
    }

    /// `SCRankCharacterRow` as the live client was fed it: account "Cosmic", signed `v1`.
    fn row() -> SCRankCharacterRow {
        SCRankCharacterRow {
            account_id: 7,
            account: "Cosmic".into(),
            character_id: 0x0A0B_0C0D,
            rank_id: 1,
            world_id: 3,
            v1: -4660,
            sum_v1: true,
            v2: 2_000_000_000,
            sum_v2: false,
            recorded: 1_700_000_500,
        }
    }

    /// The one-entry/one-scope board that the live client rendered (name, world, 100 / 7).
    fn snapshot() -> SCRankSnapshotPacket {
        SCRankSnapshotPacket {
            rank_id: 1,
            error: 0,
            board_id: 1,
            snapshot_updated: 1_700_000_000,
            reset_updated: 1_700_003_600,
            entries: vec![SCRankSnapshotEntry {
                character_id: 7,
                rank_id: 1,
                world_id: 1,
                v1: 100,
                v2: 7,
                updated: 1_700_000_000,
                ranking: 1,
            }],
            scopes: vec![SCRankSnapshotScope {
                scope_id: 1,
                v1: 100,
                v2: 7,
            }],
        }
    }

    #[test]
    fn packet_defs_use_the_verified_opcodes() {
        assert_eq!(SCRankRewardMailPacket::OPCODE, 0x1f9);
        assert_eq!(SCRankCharacterPacket::OPCODE, 0x1fa);
        assert_eq!(SCRankSnapshotPacket::OPCODE, 0x1fb);
        assert_eq!(CS_RANK_SNAPSHOT_PACKET_OPCODE, 0x130);
        assert_eq!(SCRankCharacterPacket::OPCODE, sc::SCRankCharacterPacket);
        assert_eq!(SCRankSnapshotPacket::OPCODE, sc::SCRankSnapshotPacket);
        assert_eq!(SCRankRewardMailPacket::OPCODE, sc::SCRankRewardMailPacket);
        assert_eq!(SCRankCharacterPacket::LEVEL, LEVEL_PLAIN);
    }

    #[test]
    fn reward_mail_is_one_rank_id() {
        assert_eq!(
            body(&SCRankRewardMailPacket { rank_id: 2 }),
            vec![2, 0, 0, 0]
        );
    }

    #[test]
    fn character_packet_without_rows_is_byte_exact() {
        assert_eq!(
            body(&SCRankCharacterPacket {
                type_id: 0x1122_3344,
                error: 0,
                ack_time: 0,
                rows: Vec::new(),
            }),
            vec![
                0x44, 0x33, 0x22, 0x11, // type_id
                0, 0, // error
                0, 0, 0, 0, 0, 0, 0, 0, // ack_time
                0, 0, 0, 0, // count = 0
            ]
        );
    }

    #[test]
    fn character_packet_single_row_is_byte_exact() {
        let p = SCRankCharacterPacket {
            type_id: 1,
            error: 0x1234,
            ack_time: 1_700_000_000,
            rows: vec![row()],
        };
        assert_eq!(
            body(&p),
            vec![
                1, 0, 0, 0, // type_id
                0x34, 0x12, // error
                0x00, 0xF1, 0x53, 0x65, 0, 0, 0, 0, // ack_time = 1700000000
                1, 0, 0, 0, // count = 1
                7, 0, 0, 0, // account_id
                6, 0, b'C', b'o', b's', b'm', b'i', b'c', // "Cosmic"
                0x0D, 0x0C, 0x0B, 0x0A, // character_id
                1, 0, 0, 0, // rank_id (the client's board key)
                3, // world_id
                0xCC, 0xED, 0xFF, 0xFF, // v1 = -4660
                1,    // sum_v1
                0x00, 0x94, 0x35, 0x77, // v2 = 2000000000
                0,    // sum_v2
                0xF4, 0xF2, 0x53, 0x65, 0, 0, 0, 0, // recorded = 1700000500
            ]
        );
    }

    #[test]
    fn character_packet_caps_rows_at_16() {
        let rows: Vec<SCRankCharacterRow> = (0..20)
            .map(|i| SCRankCharacterRow {
                character_id: 100 + i,
                ..row()
            })
            .collect();
        let p = SCRankCharacterPacket {
            type_id: 1,
            error: 0,
            ack_time: 1_700_000_000,
            rows,
        };
        let b = body(&p);
        // 18-byte header + 16 rows of 39 bytes ("Cosmic" account), the 17th..20th are dropped.
        assert_eq!(b.len(), 18 + 16 * 39);
        assert_eq!(&b[14..18], &[16, 0, 0, 0]);
        // The last written character id is row 16's, and the body ends with its timestamp.
        assert_eq!(&b[18 + 15 * 39 + 12..18 + 15 * 39 + 16], &[115, 0, 0, 0]);
        assert_eq!(&b[b.len() - 8..], &[0xF4, 0xF2, 0x53, 0x65, 0, 0, 0, 0]);
    }

    #[test]
    fn character_packet_account_is_cut_to_the_client_buffer() {
        // 20 ASCII bytes: 16 travel, the rest is dropped.
        let mut p = SCRankCharacterPacket {
            type_id: 1,
            error: 0,
            ack_time: 0,
            rows: vec![SCRankCharacterRow {
                account: "ABCDEFGHIJKLMNOPQRST".into(),
                ..row()
            }],
        };
        let b = body(&p);
        assert_eq!(b.len(), 18 + 4 + 2 + 16 + 4 + 4 + 1 + 4 + 1 + 4 + 1 + 8);
        assert_eq!(&b[22..24], &[16, 0]);
        assert_eq!(&b[24..40], b"ABCDEFGHIJKLMNOP");

        // A multi-byte character straddling byte 16 is dropped whole: 14 'A' + 3-byte 'EUR'.
        p.rows[0].account = "AAAAAAAAAAAAAA\u{20AC}".into();
        let b = body(&p);
        assert_eq!(&b[22..24], &[14, 0]);
        assert_eq!(&b[24..38], b"AAAAAAAAAAAAAA");
        assert_eq!(b.len(), 18 + 4 + 2 + 14 + 4 + 4 + 1 + 4 + 1 + 4 + 1 + 8);
    }

    #[test]
    fn snapshot_packet_without_entries_is_byte_exact() {
        assert_eq!(
            body(&SCRankSnapshotPacket {
                rank_id: 1,
                error: 0,
                board_id: -1,
                snapshot_updated: 0,
                reset_updated: 0,
                entries: Vec::new(),
                scopes: Vec::new(),
            }),
            vec![
                1, 0, 0, 0, // rank_id
                0, 0, // error
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // board_id = -1
                0, 0, 0, 0, 0, 0, 0, 0, // snapshot_updated
                0, 0, 0, 0, 0, 0, 0, 0, // reset_updated
                0, 0, 0, 0, // entry count
                0, 0, 0, 0, // scope count
            ]
        );
    }

    #[test]
    fn snapshot_packet_one_entry_one_scope_is_byte_exact() {
        // The live-verified shape: the client showed name/world/v1/v2/date and scope 1/100/7.
        assert_eq!(
            body(&snapshot()),
            vec![
                1, 0, 0, 0, // rank_id (the board key)
                0, 0, // error
                1, 0, 0, 0, 0, 0, 0, 0, // board_id (opaque i64)
                0x00, 0xF1, 0x53, 0x65, 0, 0, 0, 0, // snapshot_updated
                0x10, 0xFF, 0x53, 0x65, 0, 0, 0, 0, // reset_updated
                1, 0, 0, 0, // entry count
                7, 0, 0, 0, // character_id
                1, 0, 0, 0, // rank_id
                1, // world_id
                100, 0, 0, 0, // v1
                7, 0, 0, 0, // v2
                0x00, 0xF1, 0x53, 0x65, 0, 0, 0, 0, // updated
                1, 0, 0, 0, // ranking
                1, 0, 0, 0, // scope count
                1, 0, 0, 0, // scope_id
                100, 0, 0, 0, // scope v1
                7, 0, 0, 0, // scope v2
            ]
        );
    }

    #[test]
    fn snapshot_packet_keeps_signed_scores() {
        let mut p = snapshot();
        p.entries[0].v1 = i32::MIN;
        p.entries[0].v2 = -1;
        p.scopes[0].v1 = -1;
        p.scopes[0].v2 = i32::MIN;
        let b = body(&p);
        let entry = 34; // header ends after the entry count
        assert_eq!(&b[entry + 9..entry + 13], &[0x00, 0x00, 0x00, 0x80]);
        assert_eq!(&b[entry + 13..entry + 17], &[0xFF, 0xFF, 0xFF, 0xFF]);
        let scope = entry + 29 + 4;
        assert_eq!(&b[scope + 4..scope + 8], &[0xFF, 0xFF, 0xFF, 0xFF]);
        assert_eq!(&b[scope + 8..scope + 12], &[0x00, 0x00, 0x00, 0x80]);
    }

    #[test]
    fn snapshot_packet_caps_entries_and_scopes_at_100() {
        let p = SCRankSnapshotPacket {
            entries: (0..150)
                .map(|i| SCRankSnapshotEntry {
                    character_id: 1_000 + i,
                    ..snapshot().entries[0].clone()
                })
                .collect(),
            scopes: (0..120)
                .map(|i| SCRankSnapshotScope {
                    scope_id: 2_000 + i,
                    ..snapshot().scopes[0].clone()
                })
                .collect(),
            ..snapshot()
        };
        let b = body(&p);
        // 4 + 2 + 8 + 8 + 8 + 4 header, 100 * 29 entries, 4-count, 100 * 12 scopes.
        assert_eq!(b.len(), 34 + 100 * 29 + 4 + 100 * 12);
        assert_eq!(&b[30..34], &[100, 0, 0, 0]);
        assert_eq!(&b[34 + 100 * 29..34 + 100 * 29 + 4], &[100, 0, 0, 0]);
        // The last written entry/scope are the 100th of each list.
        assert_eq!(
            &b[34 + 99 * 29..34 + 99 * 29 + 4],
            &(1_099u32).to_le_bytes()[..]
        );
        let scopes = 34 + 100 * 29 + 4;
        assert_eq!(
            &b[scopes + 99 * 12..scopes + 99 * 12 + 4],
            &(2_099u32).to_le_bytes()[..]
        );
    }

    #[test]
    fn snapshot_request_decodes_exactly_four_bytes() {
        assert_eq!(
            CSRankSnapshotRequest::decode(&[1, 0, 0, 0]).unwrap(),
            CSRankSnapshotRequest { rank_id: 1 }
        );
        assert_eq!(
            CSRankSnapshotRequest::decode(&[0xFF, 0xFF, 0xFF, 0xFF]).unwrap(),
            CSRankSnapshotRequest { rank_id: u32::MAX }
        );
        // Truncated and trailing bodies are both refused.
        assert!(CSRankSnapshotRequest::decode(&[]).is_err());
        assert!(CSRankSnapshotRequest::decode(&[1, 0, 0]).is_err());
        assert!(CSRankSnapshotRequest::decode(&[1, 0, 0, 0, 0]).is_err());
    }

    #[test]
    fn snapshot_request_reads_from_a_stream() {
        let mut s = PacketStream::from_bytes(vec![4, 0, 0, 0]);
        assert_eq!(CSRankSnapshotRequest::read(&mut s).unwrap().rank_id, 4);
    }
}
