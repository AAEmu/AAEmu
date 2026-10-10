//! `Scripts/Commands/Appellation.cs`.

use super::prelude::*;

/// `Appellation`.
pub struct Appellation;

impl Appellation {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Appellation {
    fn command_names(&self) -> Vec<String> {
        names(&["title", "addtitle", "add_title", "appellation"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Appellation")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
