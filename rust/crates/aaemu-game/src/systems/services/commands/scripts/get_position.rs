//! `Scripts/Commands/GetPosition.cs`.

use super::prelude::*;

/// `GetPosition`.
pub struct GetPosition;

impl GetPosition {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for GetPosition {
    fn command_names(&self) -> Vec<String> {
        names(&["position", "pos"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: GetPosition")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
