//! `Scripts/Commands/Damage.cs`.

use super::prelude::*;

/// `Damage`.
pub struct Damage;

impl Damage {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Damage {
    fn command_names(&self) -> Vec<String> {
        names(&["damage"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Damage")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
