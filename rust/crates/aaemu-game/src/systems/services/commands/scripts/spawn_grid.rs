//! `Scripts/Commands/SpawnGrid.cs`.

use super::prelude::*;

/// `SpawnGrid`.
pub struct SpawnGrid;

impl SpawnGrid {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for SpawnGrid {
    fn command_names(&self) -> Vec<String> {
        names(&["spawngrid", "spawngroup"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: SpawnGrid")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
