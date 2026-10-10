//! AAEmu login server — a port of the C# `AAEmu.Login` project.
//!
//! * Client listener (C2L/L2C, `Network` config section) and internal listener for game servers
//!   (G2L/L2G, `InternalNetwork` section); both use `[u16 len][u16 opcode][body]` frames.
//! * Targets the 1.2 client protocol (`client_12_r208022` opcodes).
//! * Auth flows: EU password (Trion / Mail.Ru), reconnect tokens, Korea V2 challenge with
//!   OTP / PC-cert / ARS second factors.
//! * MySQL (`aaemu_login` schema) via sqlx runtime queries.

pub mod auth;
pub mod backend;
pub mod config;
pub mod controllers;
pub mod db;
pub mod handlers;
pub mod health;
pub mod id_manager;
pub mod lenient;
pub mod models;
pub mod network;
pub mod packets;
pub mod server;
pub mod services;
#[cfg(test)]
pub(crate) mod test_support;
