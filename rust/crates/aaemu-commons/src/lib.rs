//! Shared code for the AAEmu Rust servers (port of `AAEmu.Commons`, plus the client-data
//! access layer from `AAEmu.Game/IO`).
//!
//! Not ported: `AAEmu.Commons.Conversion` (`EndianBitConverter` and friends) — subsumed by
//! `{int}::{to,from}_{le,be}_bytes` in std.

pub mod aapak;
pub mod client_files;
pub mod dotnet_fmt;
pub mod error;
pub mod file_manager;
pub mod helpers;
pub mod logging;
pub mod models;
pub mod mysql;
pub mod net;
pub mod packet_stream;
pub mod utils;

#[cfg(test)]
mod test_util;

pub use aapak::AAPak;
pub use client_files::{ClientDataConfig, ClientFileManager, ClientSource, ClientSourceType};
pub use error::GameError;
pub use packet_stream::{Marshal, MarshalError, PacketStream, Unmarshal};
