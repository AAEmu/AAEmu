//! `Scripts/Commands/AddBuff.cs`.

use super::prelude::*;

/// `AddBuff`.
pub struct AddBuff;

impl AddBuff {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddBuff {
    fn command_names(&self) -> Vec<String> {
        names(&["buff", "addbuff", "add_buff", "buffs"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddBuff")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
