//! Rust port of `AAEmu.Game` — phase 1: network core, login-server link and the lobby /
//! enter-world flow up to the character list. See `PORTING.md` for the subsystem status.

pub mod config;
pub mod data;
pub mod db;
pub mod error;
pub mod login;
pub mod managers;
pub mod models;
pub mod net;
pub mod opcodes;
pub mod packets;
pub mod server;

pub use error::{GameError, Result};
pub use server::GameServer;

#[cfg(test)]
mod flow_tests;
