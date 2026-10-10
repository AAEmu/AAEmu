//! `Scripts/Commands/TestNavMesh.cs`.

use super::prelude::*;

/// `TestNavMesh`.
pub struct TestNavMesh;

impl TestNavMesh {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestNavMesh {
    fn command_names(&self) -> Vec<String> {
        names(&["testnavmesh", "test_navmesh"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestNavMesh")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
