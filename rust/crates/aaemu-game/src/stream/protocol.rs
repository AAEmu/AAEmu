//! `StreamNetwork` packet table + `StreamProtocolHandler.OnReceive` dispatch (after
//! reassembly, which [`aaemu_commons::net::FrameCodec`] does).

use aaemu_commons::PacketStream;

use crate::error::Result;
use crate::server::GameServer;
use crate::stream::c2s::*;
use crate::stream::connection::StreamConnection;
use crate::stream::packet::decode_frame;

/// Generates the opcode table and the dispatcher from one list (the `StreamNetwork()`
/// registrations, in C# order).
macro_rules! stream_packets {
    ($($ty:ident),* $(,)?) => {
        /// `(opcode, name)` of every registered C->S stream packet.
        pub const REGISTERED: &[(u16, &str)] = &[$(($ty::OPCODE, $ty::NAME)),*];

        async fn dispatch(
            server: &GameServer,
            conn: &mut StreamConnection,
            opcode: u16,
            mut body: PacketStream,
        ) -> Option<Result<()>> {
            $(
                if opcode == $ty::OPCODE {
                    let packet = match $ty::read(&mut body) {
                        Ok(p) => p,
                        Err(e) => {
                            tracing::error!("StreamPacket: C->S {} failed to read: {e}", $ty::NAME);
                            return Some(Err(e));
                        }
                    };
                    tracing::debug!("StreamPacket: C->S type {:03X} {:?}", opcode, packet);
                    return Some(packet.handle(server, conn).await);
                }
            )*
            None
        }
    };
}

stream_packets!(
    CTJoinPacket,
    CTRequestCellPacket,
    CTRequestEmblemPacket,
    CTCancelCellPacket,
    CTContinuePacket,
    CTUccComplexPacket,
    CTUccStringPacket,
    CTUccPositionPacket,
    CTUccCharacterNamePacket,
    CTQueryCharNamePacket,
    CTUploadEmblemStreamPacket,
    CTEmblemStreamUploadStatusPacket,
    CTStartUploadEmblemStreamPacket,
    CTEmblemStreamDownloadStatusPacket,
    CTItemUccPacket,
    CTEmblemPartDownloadedPacket,
    CTUccComplexCheckValidPacket,
);

/// Decodes and dispatches one frame payload (`[u16 opcode][body]`) for `conn`. An error means
/// the connection must be shut down (the C# `OnReceive` catch-all). Unknown opcodes are logged
/// with a hex dump and ignored (`HandleUnknownPacket`).
pub async fn handle_frame(
    server: &GameServer,
    conn: &mut StreamConnection,
    payload: &[u8],
) -> Result<()> {
    let (opcode, body) = decode_frame(payload);
    if !REGISTERED.iter().any(|&(op, _)| op == opcode) {
        let dump: String = body.unread().iter().map(|b| format!("{b:02x} ")).collect();
        tracing::error!(
            "Unknown packet 0x{:02x} from {}:\n{}",
            opcode,
            conn.ip,
            dump
        );
        return Ok(());
    }
    dispatch(server, conn, opcode, body).await.unwrap_or(Ok(()))
}
