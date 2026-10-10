//! `Scripts/Commands/TestTransfer.cs`.

use super::prelude::*;

/// `TestTransfer`.
pub struct TestTransfer;

impl TestTransfer {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestTransfer {
    fn command_names(&self) -> Vec<String> {
        names(&["testtransfer", "test_transfer"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestTransfer")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
