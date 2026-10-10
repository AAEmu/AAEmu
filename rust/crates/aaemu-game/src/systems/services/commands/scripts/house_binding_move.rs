//! `Scripts/Commands/HouseBindingMove.cs`.

use super::prelude::*;

/// `HouseBindingMove`.
pub struct HouseBindingMove;

impl HouseBindingMove {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for HouseBindingMove {
    fn command_names(&self) -> Vec<String> {
        names(&["house_binding_move", "housebindingmove"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: HouseBindingMove")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
