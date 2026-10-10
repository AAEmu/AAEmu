//! `Scripts/SubCommands/Doodads/**` and `Scripts/Commands/DoodadCmd.cs`.

use super::super::prelude::*;

/// `DoodadCmd`.
pub fn doodad_cmd() -> RootCommand {
    RootCommand::new(
        &["doodad"],
        "[Doodad]",
        "Root command to manage Doodads",
        "",
    )
    // PORT-TODO: Register(...) the sub commands in C# order.
}
