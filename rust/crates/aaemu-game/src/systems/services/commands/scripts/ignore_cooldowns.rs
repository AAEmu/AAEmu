//! `Scripts/Commands/IgnoreCooldowns.cs`.

use super::prelude::*;

/// `IgnoreCooldowns`.
pub struct IgnoreCooldowns;

impl IgnoreCooldowns {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for IgnoreCooldowns {
    fn command_names(&self) -> Vec<String> {
        names(&[
            "ignoreskillcds",
            "disablecooldowns",
            "ignorecooldowns",
            "ignorecd",
        ])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: IgnoreCooldowns")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
