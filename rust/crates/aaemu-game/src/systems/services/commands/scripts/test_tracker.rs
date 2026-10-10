//! `Scripts/Commands/TestTracker.cs`.

use super::prelude::*;

/// `TestTracker`.
pub struct TestTracker;

impl TestTracker {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestTracker {
    fn command_names(&self) -> Vec<String> {
        names(&["testtracker", "track", "tt"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestTracker")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
