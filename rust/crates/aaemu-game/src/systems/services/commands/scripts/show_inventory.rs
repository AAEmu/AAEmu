//! `Scripts/Commands/ShowInventory.cs`.

use super::prelude::*;

/// `ShowInventory`.
pub struct ShowInventory;

impl ShowInventory {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ShowInventory {
    fn command_names(&self) -> Vec<String> {
        names(&[
            "inventory",
            "showinv",
            "show_inv",
            "showinventory",
            "show_inventory",
        ])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ShowInventory")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
