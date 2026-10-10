//! `Scripts/Commands/GetAttribute.cs`.

use super::prelude::*;

/// `GetAttribute`.
pub struct GetAttribute;

impl GetAttribute {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for GetAttribute {
    fn command_names(&self) -> Vec<String> {
        names(&["getattribute", "getattr", "attr"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: GetAttribute")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
