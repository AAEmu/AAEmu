//! `Scripts/Commands/WaterDebugCmd.cs`.

use super::prelude::*;

/// `WaterDebugCmd`.
pub struct WaterDebugCmd;

impl WaterDebugCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for WaterDebugCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["waterdebug", "water_debug"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: WaterDebugCmd")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
