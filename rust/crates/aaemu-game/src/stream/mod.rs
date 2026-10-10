//! Stream server (port of `Core/Network/Stream`, `Core/Packets/C2S`, `Core/Packets/S2C`,
//! `Core/Managers/Stream` and the cell half of `Core/Managers/World/StreamManager`).
//!
//! The client opens this second TCP connection (port `StreamNetwork.Port`, sent in
//! `X2EnterWorldResponsePacket`) and joins it with `CTJoinPacket(accountId, cookie)`, where the
//! cookie is its game connection id registered by `StreamManager.AddToken` during
//! `X2EnterWorld`. The stream carries doodad cell streaming and UCC (crest / emblem) upload and
//! download.
//!
//! Framing: `[u16 len][u16 opcode][body]` both ways (see [`packet`]).
//!
//! Game-world lookups (doodads per cell, character names, items, the active character) go
//! through the [`world::StreamWorld`] hook, which answers "not found" until those subsystems are
//! ported.

pub mod c2s;
pub mod cells;
pub mod connection;
pub mod listener;
pub mod models;
pub mod opcodes;
pub mod packet;
pub mod protocol;
pub mod s2c;
pub mod ucc;
pub mod world;

#[cfg(test)]
mod tests;
