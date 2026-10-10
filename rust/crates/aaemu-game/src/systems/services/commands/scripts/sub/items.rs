//! `Scripts/SubCommands/Items/**` and `Scripts/Commands/ItemCmd.cs`.

use super::super::prelude::*;

/// `ItemCmd`.
pub fn item_cmd() -> RootCommand {
    RootCommand::new(&["item"], "[Item]", "Root command to manage Items", "")
    // PORT-TODO: Register(...) the sub commands in C# order.
}
