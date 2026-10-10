//! `Scripts/Commands/BuildHouse.cs`.

use super::prelude::*;

/// `BuildHouse`.
pub struct BuildHouse;

impl BuildHouse {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for BuildHouse {
    fn command_names(&self) -> Vec<String> {
        names(&["build", "build_house"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: BuildHouse")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
