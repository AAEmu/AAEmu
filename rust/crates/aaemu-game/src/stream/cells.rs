//! `StreamManager.RequestCell` / `StreamManager.ContinueCell`: doodads of a world cell, sent in
//! batches of 30 (`TCDoodadStreamPacket`).

use crate::error::{GameError, Result};
use crate::server::GameServer;
use crate::stream::connection::StreamConnection;
use crate::stream::s2c::TCDoodadStreamPacket;

/// Doodads per `TCDoodadStreamPacket`.
pub const BATCH_SIZE: i32 = 30;

/// `StreamManager.RequestCell(connection, instanceId, x, y)`.
///
/// The C# compares `instanceId` with the player's instance but does nothing with the result;
/// the requested instance is used as-is. A missing world instance dereferences null in C#
/// (connection closed), which is an error here.
pub fn request_cell(
    server: &GameServer,
    conn: &mut StreamConnection,
    instance_id: u32,
    x: i32,
    y: i32,
) -> Result<()> {
    let Some(doodads) = server.streams.world().doodads_in_cell(instance_id, x, y) else {
        return Err(GameError::Protocol(format!(
            "RequestCell: world instance {instance_id} not found"
        )));
    };
    let count = doodads.len().min(BATCH_SIZE as usize);
    let first = doodads[..count].to_vec();
    let request_id = conn.next_request_id(doodads);
    conn.send(&TCDoodadStreamPacket {
        id: request_id,
        next: count as i32,
        doodads: first,
    });
    Ok(())
}

/// `StreamManager.ContinueCell(connection, requestId, next)`, faithfully including its quirks:
///
/// * the request is dropped once `next >= total`, but the packet is still sent (empty when
///   `next == total`);
/// * the batch is copied from `next - 1` (C# off-by-one: the last doodad of the previous batch is
///   repeated and the final doodad of the cell is never sent by a continuation);
/// * `next > total` makes `new Doodad[negative]` throw, and an out-of-range copy throws
///   `ArgumentException`; both close the connection in C# and are errors here.
pub fn continue_cell(conn: &mut StreamConnection, request_id: i32, next: i32) -> Result<()> {
    let Some(doodads) = conn.request(request_id).cloned() else {
        return Ok(());
    };
    let total = doodads.len() as i32;
    if next >= total {
        conn.remove_request(request_id);
    }
    let count = total.wrapping_sub(next).min(BATCH_SIZE);
    if count < 0 {
        return Err(GameError::Protocol(format!(
            "ContinueCell: negative batch size {count} (request {request_id}, next {next})"
        )));
    }
    let src = if next > 0 { next - 1 } else { 0 };
    if i64::from(src) + i64::from(count) > i64::from(total) {
        return Err(GameError::Protocol(format!(
            "ContinueCell: copy {src}+{count} beyond {total} doodads"
        )));
    }
    let batch = doodads[src as usize..(src + count) as usize].to_vec();
    conn.send(&TCDoodadStreamPacket {
        id: request_id,
        next: next.wrapping_add(count),
        doodads: batch,
    });
    Ok(())
}
