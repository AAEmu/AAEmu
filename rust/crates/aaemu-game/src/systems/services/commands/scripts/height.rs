//! `Scripts/Commands/Height.cs`.

use super::prelude::*;

/// `Height`.
pub struct Height;

impl Height {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Height {
    fn command_names(&self) -> Vec<String> {
        names(&["height"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Height")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
