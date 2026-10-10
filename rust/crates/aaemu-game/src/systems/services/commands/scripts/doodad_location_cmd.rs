//! `Scripts/Commands/DoodadLocationCmd.cs`.

use super::prelude::*;

/// `DoodadLocationCmd`.
pub struct DoodadLocationCmd;

impl DoodadLocationCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for DoodadLocationCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["doodad_location", "doodadlocation", "dloc"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: DoodadLocationCmd")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
