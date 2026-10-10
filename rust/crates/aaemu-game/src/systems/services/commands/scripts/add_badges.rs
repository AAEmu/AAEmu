//! `Scripts/Commands/AddBadges.cs`.

use super::prelude::*;

/// `AddBadges`.
pub struct AddBadges;

impl AddBadges {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddBadges {
    fn command_names(&self) -> Vec<String> {
        names(&["vocation", "vocationpoints", "add_vp", "add_vb"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddBadges")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
