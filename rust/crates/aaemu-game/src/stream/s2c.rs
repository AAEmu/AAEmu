//! S->C stream packets (`AAEmu.Game.Core.Packets.S2C`, the `TC*` packets). Every packet of the
//! C# folder is here, including the ones the C# server never sends (`TCDoodadIdsPacket`,
//! `TCDownloadEmblemPacket`, `TCHouseFarmPacket`, `TCUccPositionPacket`, `TCUccStringPacket`).

use aaemu_commons::{Marshal, PacketStream};

use crate::error::{GameError, Result};
use crate::stream::models::Ucc;
use crate::stream::opcodes::tc;
use crate::stream::packet::stream_packet_def;
use crate::stream::world::{ItemUcc, StreamDoodad};

/// `EmblemStreamStatus`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum EmblemStreamStatus {
    Continue = 0,
    Start = 1,
    End = 2,
}

/// `TCJoinResponsePacket` (0x001): `0` = accepted, `1` = rejected.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCJoinResponsePacket {
    pub response: u8,
}
stream_packet_def!(TCJoinResponsePacket, tc::TC_JOIN_RESPONSE);

impl Marshal for TCJoinResponsePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.response);
    }
}

/// `TCDoodadStreamPacket` (0x002).
#[derive(Debug, Clone, PartialEq)]
pub struct TCDoodadStreamPacket {
    pub id: i32,
    pub next: i32,
    pub doodads: Vec<StreamDoodad>,
}
stream_packet_def!(TCDoodadStreamPacket, tc::TC_DOODAD_STREAM);

impl Marshal for TCDoodadStreamPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.id)
            .write_i32(self.next)
            .write_i32(self.doodads.len() as i32);
        for d in &self.doodads {
            s.write_bc(d.obj_id)
                .write_u32(d.template_id)
                .write_position(d.x, d.y, d.z)
                .write_i16(d.roll)
                .write_i16(d.pitch)
                .write_i16(d.yaw)
                .write_f32(d.scale)
                .write_u32(d.func_group_id) // doodad_func_groups Id
                .write_u32(d.time_left) // growing
                .write_i64(d.plant_time); // plantTime
        }
    }
}

/// `TCDoodadIdsPacket` (0x003). Not sent anywhere in the C# server.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCDoodadIdsPacket {
    pub id: i32,
    pub next_id: i32,
    pub total: i32,
    pub obj_ids: Vec<u32>,
}
stream_packet_def!(TCDoodadIdsPacket, tc::TC_DOODAD_IDS);

impl Marshal for TCDoodadIdsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.id)
            .write_i32(self.next_id)
            .write_i32(self.total)
            .write_i32(self.obj_ids.len() as i32);
        for &id in &self.obj_ids {
            s.write_bc(id);
        }
    }
}

/// `TCDownloadEmblemPacket` (0x004). Stub in C# (all zeroes); never sent.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TCDownloadEmblemPacket;
stream_packet_def!(TCDownloadEmblemPacket, tc::TC_DOWNLOAD_EMBLEM);

impl Marshal for TCDownloadEmblemPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(0) // type
            .write_i32(0) // size
            .write_u64(0); // modified
    }
}

/// `TCUccComplexPacket` (0x005).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCUccComplexPacket {
    pub id: u32,
    /// `Modified` as `Helpers.UnixTime`.
    pub modified: i64,
}
stream_packet_def!(TCUccComplexPacket, tc::TC_UCC_COMPLEX);

impl TCUccComplexPacket {
    pub fn new(ucc: &Ucc) -> Self {
        Self {
            id: ucc.id,
            modified: ucc.modified.unix_time(),
        }
    }
}

impl Marshal for TCUccComplexPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u64(u64::from(self.id)) // type
            .write_u64(0) // type unk
            .write_u64(0) // type unk
            .write_u64(u64::from(self.id)) // type
            .write_i64(self.modified); // modified
    }
}

/// `TCUccStringPacket` (0x006). Stub in C# (all zeroes); never sent.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TCUccStringPacket;
stream_packet_def!(TCUccStringPacket, tc::TC_UCC_STRING);

impl Marshal for TCUccStringPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(0) // type
            .write_u16(0) // "data" length
            .write_u64(0); // modified
    }
}

/// `TCUccPositionPacket` (0x007). Stub in C# (all zeroes); never sent.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TCUccPositionPacket;
stream_packet_def!(TCUccPositionPacket, tc::TC_UCC_POSITION);

impl Marshal for TCUccPositionPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(0) // type
            .write_i64(0) // x
            .write_i64(0) // y
            .write_f32(0.0) // z
            .write_u64(0); // modified
    }
}

/// `TCUccCharNamePacket` (0x008).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCUccCharNamePacket {
    pub id: u32,
    pub name: String,
}
stream_packet_def!(TCUccCharNamePacket, tc::TC_UCC_CHAR_NAME);

impl Marshal for TCUccCharNamePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_str(&self.name);
    }
}

/// `TCCharNameQueriedPacket` (0x009).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCCharNameQueriedPacket {
    pub id: u32,
    pub name: String,
}
stream_packet_def!(TCCharNameQueriedPacket, tc::TC_CHAR_NAME_QUERIED);

impl Marshal for TCCharNameQueriedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_str(&self.name);
    }
}

/// `TCEmblemStreamRecvStatusPacket` (0x00a): upload progress.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCEmblemStreamRecvStatusPacket {
    pub status: EmblemStreamStatus,
}
stream_packet_def!(
    TCEmblemStreamRecvStatusPacket,
    tc::TC_EMBLEM_STREAM_RECV_STATUS
);

impl Marshal for TCEmblemStreamRecvStatusPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.status as u8).write_i32(0);
    }
}

/// `TCEmblemStreamSendStatusPacket` (0x00b): download progress + the UCC appearance.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCEmblemStreamSendStatusPacket {
    /// The UCC without its image data (only its length is sent).
    pub ucc: Ucc,
    /// `CustomUcc.Data.Count`, 0 for a `DefaultUcc`.
    pub total: i32,
    pub status: EmblemStreamStatus,
}
stream_packet_def!(
    TCEmblemStreamSendStatusPacket,
    tc::TC_EMBLEM_STREAM_SEND_STATUS
);

impl TCEmblemStreamSendStatusPacket {
    pub fn new(ucc: &Ucc, status: EmblemStreamStatus) -> Self {
        let total = ucc.data_len() as i32;
        let mut ucc = ucc.clone();
        ucc.data = Vec::new();
        Self { ucc, total, status }
    }
}

impl Marshal for TCEmblemStreamSendStatusPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(i64::from(self.ucc.id)) // type
            .write_i32(self.total); // total data bytes
        self.ucc.write(s);
        s.write_u8(self.status as u8);
    }
}

/// `TCEmblemStreamDownloadPacket` (0x00c): one chunk of a custom UCC image.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCEmblemStreamDownloadPacket {
    pub index: i32,
    /// Written as `i32` and again as `i16`; can be negative (C# writes it unchecked).
    pub size: i32,
    pub data: Vec<u8>,
}
stream_packet_def!(TCEmblemStreamDownloadPacket, tc::TC_EMBLEM_STREAM_DOWNLOAD);

impl TCEmblemStreamDownloadPacket {
    /// `BufferSize = 1024 * 3`.
    pub const BUFFER_SIZE: i32 = 3072;

    /// Chunk `index` of `ucc`'s data (C# computes it in `Write`). A non-custom UCC or one without
    /// data yields `index, 0, 0`. A negative start offset with a positive size makes C#'s
    /// `List.GetRange` throw (closing the connection); that is an error here.
    pub fn new(ucc: &Ucc, index: i32) -> Result<Self> {
        if !ucc.is_custom() || ucc.data.is_empty() {
            return Ok(Self {
                index,
                size: 0,
                data: Vec::new(),
            });
        }
        let count = ucc.data.len() as i32;
        let start = index.wrapping_mul(Self::BUFFER_SIZE);
        let size = count.wrapping_sub(start).min(Self::BUFFER_SIZE);
        let data = if size > 0 {
            if start < 0 {
                return Err(GameError::Protocol(format!(
                    "UCC {} chunk {index}: GetRange({start}, {size}) out of range",
                    ucc.id
                )));
            }
            ucc.data[start as usize..(start + size) as usize].to_vec()
        } else {
            Vec::new()
        };
        Ok(Self { index, size, data })
    }
}

impl Marshal for TCEmblemStreamDownloadPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.index)
            .write_i32(self.size)
            .write_i16(self.size as i16)
            .write_raw(&self.data);
    }
}

/// `TCItemUccDataPacket` (0x00d).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCItemUccDataPacket {
    pub player_id: u32,
    pub items: Vec<ItemUcc>,
}
stream_packet_def!(TCItemUccDataPacket, tc::TC_ITEM_UCC_DATA);

impl Marshal for TCItemUccDataPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.player_id)
            .write_i32(self.items.len() as i32);
        for item in &self.items {
            s.write_u64(item.item_id).write_u64(item.ucc_id);
        }
    }
}

/// `TCHouseFarmPacket` (0x00e). Empty body in C# (only the client-side reader is documented).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TCHouseFarmPacket;
stream_packet_def!(TCHouseFarmPacket, tc::TC_HOUSE_FARM);

impl Marshal for TCHouseFarmPacket {
    fn write(&self, _s: &mut PacketStream) {}
}

/// `TCUccComplexCheckValidPacket` (0x00f).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TCUccComplexCheckValidPacket {
    pub id: u64,
    pub is_valid: bool,
}
stream_packet_def!(TCUccComplexCheckValidPacket, tc::TC_UCC_COMPLEX_CHECK_VALID);

impl Marshal for TCUccComplexCheckValidPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u64(self.id).write_bool(self.is_valid);
    }
}
