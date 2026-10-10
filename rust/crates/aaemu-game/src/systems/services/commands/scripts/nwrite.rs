//! `Scripts/Commands/Nwrite.cs`.

use super::prelude::*;

/// `Nwrite`.
pub struct Nwrite;

impl Nwrite {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Nwrite {
    fn command_names(&self) -> Vec<String> {
        names(&["nwrite", "nw"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Nwrite")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
