//! `Scripts/SubCommands/Slaves/**` and `Scripts/Commands/SlaveCmd.cs`.

use super::super::prelude::*;

/// `SlaveCmd`.
pub fn slave_cmd() -> RootCommand {
    RootCommand::new(&["slave"], "[slave]", "Root command to manage Slaves", "")
    // PORT-TODO: Register(...) the sub commands in C# order.
}
