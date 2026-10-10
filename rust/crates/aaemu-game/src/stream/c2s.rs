//! C->S stream packets (`AAEmu.Game.Core.Packets.C2S`, the `CT*` packets) and their behaviour.
//!
//! Each packet has a `read` (C# `Read` field order) and a `handle` (the side effects C# runs at
//! the end of `Read`). A handler error closes the stream connection, like the
//! `StreamProtocolHandler.OnReceive` catch-all (`connection.Shutdown()`).

use aaemu_commons::PacketStream;

use crate::error::{GameError, Result};
use crate::managers::stream::JoinResult;
use crate::server::GameServer;
use crate::stream::cells;
use crate::stream::connection::StreamConnection;
use crate::stream::models::{Ucc, UccPart, UccType};
use crate::stream::opcodes::ct;
use crate::stream::s2c::{
    TCCharNameQueriedPacket, TCItemUccDataPacket, TCJoinResponsePacket, TCUccCharNamePacket,
};
use crate::stream::ucc::active_character;

/// A C->S stream packet: opcode, name and the body reader.
pub trait StreamClientPacket: Sized + std::fmt::Debug {
    const OPCODE: u16;
    const NAME: &'static str;
    fn read(s: &mut PacketStream) -> Result<Self>;
}

macro_rules! client_packet {
    ($ty:ident, $op:expr, |$s:ident| $body:block) => {
        impl StreamClientPacket for $ty {
            const OPCODE: u16 = $op;
            const NAME: &'static str = stringify!($ty);
            fn read($s: &mut PacketStream) -> Result<Self> {
                Ok($body)
            }
        }
    };
}

/// `CTJoinPacket` (0x001): `StreamManager.Login(connection, accountId, cookie)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTJoinPacket {
    pub account_id: u32,
    /// The game connection id handed out in `X2EnterWorldResponsePacket`.
    pub cookie: u32,
}
client_packet!(CTJoinPacket, ct::CT_JOIN, |s| {
    CTJoinPacket {
        account_id: s.read_u32()?,
        cookie: s.read_u32()?,
    }
});

impl CTJoinPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        let result = server.streams.login(self.account_id, self.cookie);
        if let JoinResult::Accepted { game_connection_id } = result {
            // connection.GameConnection = GameConnectionTable.GetConnection(token)
            conn.game_connection_id = server.connections.get(game_connection_id).map(|h| h.id);
        }
        conn.send(&TCJoinResponsePacket {
            response: result.response(),
        });
        Ok(())
    }
}

/// `CTRequestCellPacket` (0x002).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTRequestCellPacket {
    pub instance_id: u32,
    pub x: i32,
    pub y: i32,
}
client_packet!(CTRequestCellPacket, ct::CT_REQUEST_CELL, |s| {
    CTRequestCellPacket {
        instance_id: s.read_u32()?,
        x: s.read_i32()?,
        y: s.read_i32()?,
    }
});

impl CTRequestCellPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        tracing::debug!(
            "CTRequestCellPacket #.{} ({},{})",
            self.instance_id,
            self.x,
            self.y
        );
        cells::request_cell(server, conn, self.instance_id, self.x, self.y)
    }
}

/// `CTRequestEmblemPacket` (0x003): `UccManager.RequestUcc`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTRequestEmblemPacket {
    pub ucc_id: u64,
}
client_packet!(CTRequestEmblemPacket, ct::CT_REQUEST_EMBLEM, |s| {
    CTRequestEmblemPacket {
        ucc_id: s.read_u64()?,
    }
});

impl CTRequestEmblemPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        server.streams.ucc.request_ucc(server, conn, self.ucc_id)
    }
}

/// `CTCancelCellPacket` (0x004). Read only (no behaviour in C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTCancelCellPacket {
    pub i: u32,
    pub x: i32,
    pub y: i32,
}
client_packet!(CTCancelCellPacket, ct::CT_CANCEL_CELL, |s| {
    CTCancelCellPacket {
        i: s.read_u32()?,
        x: s.read_i32()?,
        y: s.read_i32()?,
    }
});

impl CTCancelCellPacket {
    pub async fn handle(self, _server: &GameServer, _conn: &mut StreamConnection) -> Result<()> {
        Ok(())
    }
}

/// `CTContinuePacket` (0x005): `StreamManager.ContinueCell`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTContinuePacket {
    pub id: i32,
    pub next: i32,
}
client_packet!(CTContinuePacket, ct::CT_CONTINUE, |s| {
    CTContinuePacket {
        id: s.read_i32()?,
        next: s.read_i32()?,
    }
});

impl CTContinuePacket {
    pub async fn handle(self, _server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        cells::continue_cell(conn, self.id, self.next)
    }
}

/// `CTUccComplexPacket` (0x006): `UccManager.UccComplex`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTUccComplexPacket {
    pub id: u64,
}
client_packet!(CTUccComplexPacket, ct::CT_UCC_COMPLEX, |s| {
    CTUccComplexPacket { id: s.read_u64()? }
});

impl CTUccComplexPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        server.streams.ucc.ucc_complex(conn, self.id);
        Ok(())
    }
}

/// `CTUccStringPacket` (0x007). Read only (no behaviour in C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTUccStringPacket {
    pub id: i64,
}
client_packet!(CTUccStringPacket, ct::CT_UCC_STRING, |s| {
    CTUccStringPacket { id: s.read_i64()? }
});

impl CTUccStringPacket {
    pub async fn handle(self, _server: &GameServer, _conn: &mut StreamConnection) -> Result<()> {
        Ok(())
    }
}

/// `CTUccPositionPacket` (0x008). Read only (no behaviour in C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTUccPositionPacket {
    pub id: i64,
}
client_packet!(CTUccPositionPacket, ct::CT_UCC_POSITION, |s| {
    CTUccPositionPacket { id: s.read_i64()? }
});

impl CTUccPositionPacket {
    pub async fn handle(self, _server: &GameServer, _conn: &mut StreamConnection) -> Result<()> {
        Ok(())
    }
}

/// `CTUccCharacterNamePacket` (0x009): `TCUccCharNamePacket` when the name is known.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTUccCharacterNamePacket {
    pub id: u32,
}
client_packet!(CTUccCharacterNamePacket, ct::CT_UCC_CHARACTER_NAME, |s| {
    CTUccCharacterNamePacket { id: s.read_u32()? }
});

impl CTUccCharacterNamePacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        let name = server.streams.world().character_name(self.id);
        if let Some(name) = &name {
            conn.send(&TCUccCharNamePacket {
                id: self.id,
                name: name.clone(),
            });
        }
        tracing::debug!(
            "UccCharacterName, Id: {}, Name: {}",
            self.id,
            name.unwrap_or_default()
        );
        Ok(())
    }
}

/// `CTQueryCharNamePacket` (0x00a): `TCCharNameQueriedPacket` when the name is known.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTQueryCharNamePacket {
    pub id: u32,
}
client_packet!(CTQueryCharNamePacket, ct::CT_QUERY_CHAR_NAME, |s| {
    CTQueryCharNamePacket { id: s.read_u32()? }
});

impl CTQueryCharNamePacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        let name = server.streams.world().character_name(self.id);
        if let Some(name) = &name {
            conn.send(&TCCharNameQueriedPacket {
                id: self.id,
                name: name.clone(),
            });
        }
        tracing::debug!(
            "QueryCharName, Id: {}, Name: {}",
            self.id,
            name.unwrap_or_default()
        );
        Ok(())
    }
}

/// `CTUploadEmblemStreamPacket` (0x00c): one part of a custom UCC image.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTUploadEmblemStreamPacket {
    pub total: i32,
    pub size: i32,
    pub index: u32,
    pub data: Vec<u8>,
}
client_packet!(
    CTUploadEmblemStreamPacket,
    ct::CT_UPLOAD_EMBLEM_STREAM,
    |s| {
        let total = s.read_i32()?;
        let size = s.read_i32()?;
        let index = s.read_u32()?;
        let part_size = s.read_u16()?;
        CTUploadEmblemStreamPacket {
            total,
            size,
            index,
            data: s.read_bytes(usize::from(part_size))?, // max length 3096
        }
    }
);

impl CTUploadEmblemStreamPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        let part = UccPart {
            total: self.total,
            size: self.data.len() as i32,
            index: self.index,
            data: self.data,
        };
        tracing::warn!(
            "CTUploadEmblemStreamPacket, total:{}, size:{}, index:{}",
            part.total,
            part.size,
            part.index
        );
        server.streams.ucc.upload_part(server, conn, part).await
    }
}

/// `CTEmblemStreamUploadStatusPacket` (0x00d): `UccManager.ConfirmDefaultUcc`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTEmblemStreamUploadStatusPacket {
    pub status: u8,
}
client_packet!(
    CTEmblemStreamUploadStatusPacket,
    ct::CT_EMBLEM_STREAM_UPLOAD_STATUS,
    |s| {
        CTEmblemStreamUploadStatusPacket {
            status: s.read_u8()?,
        }
    }
);

impl CTEmblemStreamUploadStatusPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        server.streams.ucc.confirm_default_ucc(server, conn).await
    }
}

/// `CTStartUploadEmblemStreamPacket` (0x00e): starts a simple (`data_size == 0`) or custom UCC.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTStartUploadEmblemStreamPacket {
    /// Crest printer object id.
    pub bc: u32,
    pub ucc_id: i64,
    pub data_size: i32,
    /// Pattern / colours / modified (`DefaultUcc.Read`); `id`, `uploader_id` and `ty` are set
    /// by the handler.
    pub ucc: Ucc,
}
client_packet!(
    CTStartUploadEmblemStreamPacket,
    ct::CT_START_UPLOAD_EMBLEM_STREAM,
    |s| {
        let bc = s.read_bc()?;
        let ucc_id = s.read_i64()?;
        let data_size = s.read_i32()?;
        let ty = if data_size == 0 {
            UccType::Simple
        } else {
            UccType::Complex
        };
        let mut ucc = Ucc::new(ty, 0);
        ucc.read(s)?;
        CTStartUploadEmblemStreamPacket {
            bc,
            ucc_id,
            data_size,
            ucc,
        }
    }
);

impl CTStartUploadEmblemStreamPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        tracing::warn!(
            "Create UCC Crest, printer bc:{}, UccId:{}, dataSize:{}",
            self.bc,
            self.ucc_id,
            self.data_size
        );
        // TODO (C#): check if bc points to a Crest Printer (and you are nearby)
        let character = active_character(server, conn)?;
        let mut ucc = self.ucc;
        ucc.uploader_id = character.id;
        if self.data_size == 0 {
            server.streams.ucc.add_default_ucc(conn, ucc)
        } else {
            server.streams.ucc.start_upload(conn, self.data_size, ucc)
        }
    }
}

/// `CTEmblemStreamDownloadStatusPacket` (0x00f): `UccManager.DownloadStatus`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTEmblemStreamDownloadStatusPacket {
    pub id: u64,
    pub status: u8,
    pub count: i32,
}
client_packet!(
    CTEmblemStreamDownloadStatusPacket,
    ct::CT_EMBLEM_STREAM_DOWNLOAD_STATUS,
    |s| {
        CTEmblemStreamDownloadStatusPacket {
            id: s.read_u64()?,
            status: s.read_u8()?,
            count: s.read_i32()?,
        }
    }
);

impl CTEmblemStreamDownloadStatusPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        server
            .streams
            .ucc
            .download_status(server, conn, self.id, self.status, self.count)
    }
}

/// `CTItemUccPacket` (0x010): UCC ids of a list of items.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTItemUccPacket {
    pub player: u32,
    pub items: Vec<u64>,
}
client_packet!(CTItemUccPacket, ct::CT_ITEM_UCC, |s| {
    let player = s.read_u32()?;
    let count = s.read_u32()?;
    let mut items = Vec::new();
    for _ in 0..count {
        items.push(s.read_u64()?);
    }
    CTItemUccPacket { player, items }
});

impl CTItemUccPacket {
    /// An unknown item id dereferences null while encoding `TCItemUccDataPacket` in C#, which
    /// closes the connection; that is an error here.
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        let world = server.streams.world();
        let items = self
            .items
            .iter()
            .map(|&id| {
                world.item_ucc(id).ok_or_else(|| {
                    GameError::Protocol(format!("TCItemUccDataPacket: item {id} not found"))
                })
            })
            .collect::<Result<Vec<_>>>()?;
        conn.send(&TCItemUccDataPacket {
            player_id: self.player,
            items,
        });
        Ok(())
    }
}

/// `CTEmblemPartDownloadedPacket` (0x011): `UccManager.RequestUccPart`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTEmblemPartDownloadedPacket {
    pub previous_index: i32,
    pub previous_size: i32,
}
client_packet!(
    CTEmblemPartDownloadedPacket,
    ct::CT_EMBLEM_PART_DOWNLOADED,
    |s| {
        CTEmblemPartDownloadedPacket {
            previous_index: s.read_i32()?,
            previous_size: s.read_i32()?,
        }
    }
);

impl CTEmblemPartDownloadedPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        server
            .streams
            .ucc
            .request_ucc_part(server, conn, self.previous_index, self.previous_size)
    }
}

/// `CTUccComplexCheckValidPacket` (0x012): `UccManager.CheckUccIsValid`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CTUccComplexCheckValidPacket {
    pub id: u64,
}
client_packet!(
    CTUccComplexCheckValidPacket,
    ct::CT_UCC_COMPLEX_CHECK_VALID,
    |s| { CTUccComplexCheckValidPacket { id: s.read_u64()? } }
);

impl CTUccComplexCheckValidPacket {
    pub async fn handle(self, server: &GameServer, conn: &mut StreamConnection) -> Result<()> {
        server.streams.ucc.check_ucc_is_valid(conn, self.id);
        Ok(())
    }
}
