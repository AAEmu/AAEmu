//! Rust port of `AAEmu.Game` — phase 1: network core, login-server link and the lobby flow;
//! phase 2: character lifecycle, world entry, visibility, movement and local chat; phase 3a:
//! the shared object model (`models::unit`, `world::object`) and the pluggable subsystems
//! (`systems`); see also `CONTRIBUTING-PORT.md`. See `PORTING.md` for the subsystem status.

pub mod config;
pub mod data;
pub mod db;
pub mod db_character;
pub mod error;
pub mod login;
pub mod managers;
pub mod models;
pub mod net;
pub mod opcodes;
pub mod packets;
pub mod server;
pub mod stream;
pub mod stream_world;
pub mod systems;
pub mod world;

pub use error::{GameError, Result};
pub use server::GameServer;

#[cfg(test)]
mod flow_tests;
#[cfg(test)]
mod object_flow_tests;
#[cfg(test)]
mod test_support;
#[cfg(test)]
mod world_flow_tests;
