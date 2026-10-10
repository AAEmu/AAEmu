//! `Scripts/Commands/TestSlave.cs`.

use super::prelude::*;

/// `TestSlave`.
pub struct TestSlave;

impl TestSlave {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestSlave {
    fn command_names(&self) -> Vec<String> {
        names(&["testslave", "test_slave"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestSlave")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
