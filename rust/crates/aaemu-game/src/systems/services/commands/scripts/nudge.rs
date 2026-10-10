//! `Scripts/Commands/Nudge.cs`.

use super::prelude::*;

/// `Nudge`.
pub struct Nudge;

impl Nudge {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Nudge {
    fn command_names(&self) -> Vec<String> {
        names(&["nudge"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Nudge")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
