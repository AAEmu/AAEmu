//! `Scripts/SubCommands/Crimes/**` and `Scripts/Commands/CrimeCmd.cs`.

use super::super::prelude::*;

/// `CrimeCmd` (registered as `crime`; `CommandNames` is `["doodad"]` in C#, so the call prefix and help use `/doodad`).
pub fn crime_cmd() -> RootCommand {
    RootCommand::new(
        &["doodad"],
        "[Crime]",
        "Root command to crime related tasks",
        "",
    )
    // PORT-TODO: Register(...) the sub commands in C# order.
}
