//! `Scripts/Commands/TestAI.cs`.

use super::prelude::*;

/// `TestAI`.
pub struct TestAI;

impl TestAI {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestAI {
    fn command_names(&self) -> Vec<String> {
        names(&["testai", "ai"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestAI")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
