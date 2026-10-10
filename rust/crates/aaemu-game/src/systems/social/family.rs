//! `Core/Managers/FamilyManager` + `Models/Game/Family`.

/// `FamilyManager`.
#[derive(Debug, Default)]
pub struct FamilyManager {}

use crate::server::GameServer;
use crate::world::PlayerRef;

/// `FamilyManager.OnCharacterLogin(character)`.
pub fn on_character_login(_server: &GameServer, _p: &PlayerRef) {}

/// `FamilyManager.OnCharacterLogout(character)`.
pub fn on_character_logout(_server: &GameServer, _p: &PlayerRef) {}
