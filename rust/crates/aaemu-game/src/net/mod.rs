//! Game network core: framing, registry, per-connection state and the listener
//! (`AAEmu.Game.Core.Network.Game` + `Core.Network.Connections`).

pub mod connection;
pub mod listener;
pub mod outbox;
pub mod packet;
pub mod registry;
