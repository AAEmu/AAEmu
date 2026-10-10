//! `Scripts/Commands/AddGold.cs`.

use super::prelude::*;

/// `AddGold`.
pub struct AddGold;

impl AddGold {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddGold {
    fn command_names(&self) -> Vec<String> {
        names(&["gold", "addgold", "add_gold"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddGold")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
