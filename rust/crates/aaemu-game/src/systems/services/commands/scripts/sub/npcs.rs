//! `Scripts/SubCommands/Npcs/**` and `Scripts/Commands/NpcCmd.cs`.

use super::super::prelude::*;

/// `NpcCmd`.
pub fn npc_cmd() -> RootCommand {
    RootCommand::new(&["npc"], "[Npc]", "Root command to manage Npcs", "")
    // PORT-TODO: Register(...) the sub commands in C# order.
}
