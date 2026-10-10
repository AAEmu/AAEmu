//! `Scripts/Commands/ShipBarrierCmd.cs`.

use super::prelude::*;

/// `ShipBarrierCmd`.
pub struct ShipBarrierCmd;

impl ShipBarrierCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ShipBarrierCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["shipbarrier", "sb"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ShipBarrierCmd")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
