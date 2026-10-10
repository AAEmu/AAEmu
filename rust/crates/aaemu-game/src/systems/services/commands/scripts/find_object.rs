//! `Scripts/Commands/FindObject.cs`.

use super::prelude::*;

/// `FindObject`.
pub struct FindObject;

impl FindObject {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for FindObject {
    fn command_names(&self) -> Vec<String> {
        names(&["findobject", "findobj"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: FindObject")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
