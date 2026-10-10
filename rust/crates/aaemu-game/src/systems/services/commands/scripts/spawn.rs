//! `Scripts/Commands/Spawn.cs`.

use super::prelude::*;

/// `Spawn`.
pub struct Spawn;

impl Spawn {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Spawn {
    fn command_names(&self) -> Vec<String> {
        names(&["spawn"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Spawn")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
