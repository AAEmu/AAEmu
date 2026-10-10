//! `Scripts/Commands/DeSpawnAll.cs`.

use super::prelude::*;

/// `DeSpawnAll`.
pub struct DeSpawnAll;

impl DeSpawnAll {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for DeSpawnAll {
    fn command_names(&self) -> Vec<String> {
        names(&["despawnall", "despawn_all"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: DeSpawnAll")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
