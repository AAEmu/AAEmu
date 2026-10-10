//! `Scripts/Commands/Fly.cs`.

use super::prelude::*;

/// `Fly`.
pub struct Fly;

impl Fly {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Fly {
    fn command_names(&self) -> Vec<String> {
        names(&["fly"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Fly")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
