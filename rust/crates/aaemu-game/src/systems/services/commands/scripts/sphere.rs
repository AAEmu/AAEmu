//! `Scripts/Commands/Sphere.cs`.

use super::prelude::*;

/// `Sphere`.
pub struct Sphere;

impl Sphere {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Sphere {
    fn command_names(&self) -> Vec<String> {
        names(&["sphere"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Sphere")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
