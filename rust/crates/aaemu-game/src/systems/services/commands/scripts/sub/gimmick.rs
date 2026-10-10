//! `Scripts/SubCommands/Gimmick/**` and `Scripts/Commands/GimmickCmd.cs`.

use super::super::prelude::*;

/// `GimmickCmd`.
pub fn gimmick_cmd() -> RootCommand {
    RootCommand::new(
        &["gimmick"],
        "[Gimmick]",
        "Root command to manage Gimmicks",
        "",
    )
    // PORT-TODO: Register(...) the sub commands in C# order.
}
