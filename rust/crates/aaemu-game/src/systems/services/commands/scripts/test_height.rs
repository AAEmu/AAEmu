//! `Scripts/Commands/TestHeight.cs`.

use super::prelude::*;

/// `TestHeight`.
pub struct TestHeight;

impl TestHeight {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestHeight {
    fn command_names(&self) -> Vec<String> {
        names(&["testheightvisualizer", "test_height_visualizer"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestHeight")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
