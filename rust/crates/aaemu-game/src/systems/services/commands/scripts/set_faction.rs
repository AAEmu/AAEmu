//! `Scripts/Commands/SetFaction.cs`.

use super::prelude::*;

/// `SetFaction`.
pub struct SetFaction;

impl SetFaction {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for SetFaction {
    fn command_names(&self) -> Vec<String> {
        names(&["setfaction", "set_faction"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: SetFaction")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
