//! `Scripts/Commands/TestHouse.cs`.

use super::prelude::*;

/// `TestHouse`.
pub struct TestHouse;

impl TestHouse {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestHouse {
    fn command_names(&self) -> Vec<String> {
        names(&["house", "test_house", "testhouse"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestHouse")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
