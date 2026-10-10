//! `Core/Managers/TrialManager` + `Models/Game/Crime/{TrialData,TrialCourtRoom,...}`.
//! TODO(social): ported by the crime / trial work item.

/// `TrialManager`.
#[derive(Debug, Default)]
pub struct TrialManager {}

use crate::managers::chat::ChannelRef;
use crate::server::GameServer;
use crate::world::PlayerRef;

/// `TrialManager.HandlePlayerLogin(player)`.
pub fn handle_player_login(_server: &GameServer, _p: &PlayerRef) {}

/// `ChatManager.GetTrialChat(character)`.
pub fn trial_chat(_server: &GameServer, _p: &PlayerRef) -> Option<ChannelRef> {
    None
}
