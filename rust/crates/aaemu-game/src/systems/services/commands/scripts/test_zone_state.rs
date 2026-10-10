//! `Scripts/Commands/TestZoneState.cs`.

use super::prelude::*;

/// `TestZoneState`.
pub struct TestZoneState;

impl TestZoneState {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestZoneState {
    fn command_names(&self) -> Vec<String> {
        names(&["zonestate", "zone_state"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestZoneState")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
