//! Headless ArcheAge 1.2 (r208022) client bot for live testing of the login, game and stream
//! servers: scripted scenarios with assertions, S->C packet capture, a parity diff between two
//! server implementations and a concurrent load driver. See `rust/LIVE-TESTING.md`.

pub mod diff;
pub mod game;
pub mod load;
pub mod login;
pub mod record;
pub mod scenario;
pub mod stream;
