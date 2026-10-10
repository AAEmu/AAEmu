//! `Scripts/SubCommands/Gold/**` and `Scripts/Commands/GoldCmd.cs`.

use super::super::prelude::*;

/// `GoldCmd`.
pub fn gold_cmd() -> RootCommand {
    RootCommand::new(&["gold"], "[Gold]", "Root command to manage gold", "/")
    // PORT-TODO: Register(...) the sub commands in C# order.
}
