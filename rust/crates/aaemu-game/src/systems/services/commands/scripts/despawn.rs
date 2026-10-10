//! `Scripts/Commands/Despawn.cs`.

use super::prelude::*;

/// `Despawn`.
pub struct Despawn;

impl Despawn {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Despawn {
    fn command_names(&self) -> Vec<String> {
        names(&["despawn"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Despawn")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
