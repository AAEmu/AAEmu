//! `Scripts/Commands/Rotate.cs`.

use super::prelude::*;

/// `Rotate`.
pub struct Rotate;

impl Rotate {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Rotate {
    fn command_names(&self) -> Vec<String> {
        names(&["rotate", "lookatme"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Rotate")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
