//! `Scripts/Commands/Invisible.cs`.

use super::prelude::*;

/// `Invisible`.
pub struct Invisible;

impl Invisible {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Invisible {
    fn command_names(&self) -> Vec<String> {
        names(&["invisible"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Invisible")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
