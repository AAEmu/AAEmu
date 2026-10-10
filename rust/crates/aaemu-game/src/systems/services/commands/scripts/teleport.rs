//! `Scripts/Commands/Teleport.cs`.

use super::prelude::*;

/// `Teleport`.
pub struct Teleport;

impl Teleport {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Teleport {
    fn command_names(&self) -> Vec<String> {
        names(&["teleport"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Teleport")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
