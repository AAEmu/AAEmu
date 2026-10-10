//! `Scripts/Commands/TowerDef.cs`.

use super::prelude::*;

/// `TowerDef`.
pub struct TowerDef;

impl TowerDef {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TowerDef {
    fn command_names(&self) -> Vec<String> {
        names(&["towerdef", "tower_def"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TowerDef")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
