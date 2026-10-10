//! `Scripts/Commands/TestCombat.cs`.

use super::prelude::*;

/// `TestCombat`.
pub struct TestCombat;

impl TestCombat {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestCombat {
    fn command_names(&self) -> Vec<String> {
        names(&["testcombat", "test_combat"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestCombat")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
