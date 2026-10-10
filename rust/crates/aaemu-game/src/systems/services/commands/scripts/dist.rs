//! `Scripts/Commands/Dist.cs`.

use super::prelude::*;

/// `Dist`.
pub struct Dist;

impl Dist {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Dist {
    fn command_names(&self) -> Vec<String> {
        names(&["distance", "dist"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Dist")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
