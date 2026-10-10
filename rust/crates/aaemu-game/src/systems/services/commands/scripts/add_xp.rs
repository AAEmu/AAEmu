//! `Scripts/Commands/AddXP.cs`.

use super::prelude::*;

/// `AddXP`.
pub struct AddXP;

impl AddXP {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddXP {
    fn command_names(&self) -> Vec<String> {
        names(&["xp", "add_xp", "addxp", "givexp"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddXP")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
