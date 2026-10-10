//! The `SubCommandBase, ICommand(, ICommandV2)` root commands of `Scripts/Commands`
//! (`AStarCmd`, `CrimeCmd`, `DoodadCmd`, `FeatureCmd`, `FishFinderCmd`, `GimmickCmd`, `GoldCmd`,
//! `ItemCmd`, `NpcCmd`, `SlaveCmd`, `TimeCmd`, `WorldCmd`): a title, a description, a call prefix
//! and the registered sub commands. `CommandManager.Handle` routes them through
//! `SubCommandBase.PreExecute` (`ICommandV2`).

use std::sync::Arc;

use super::super::output::MessageOutput;
use super::super::sub::{
    AStarPathFindingSubCommand, AStarPointSubCommand, AStarViewSubCommand, FeatureCheckSubCommand,
    FeatureSetSubCommand, FishFinderSetSubCommand, TimeSetSubCommand, WorldSetSubCommand,
};
use super::super::subcommand::{SubCommand, SubCommandBase};
use super::super::{Command, CommandContext, COMMAND_PREFIX};

/// A root command: `SubCommandBase` + `ICommand` with the C# `GetCommandLineHelp` /
/// `GetCommandHelpText` / `Execute` bodies every root shares.
pub struct RootCommand {
    names: Vec<String>,
    base: SubCommandBase,
}

impl RootCommand {
    /// `Title`, `Description`, `CallPrefix = "/{CommandNames[0]}{suffix}"`.
    pub fn new(names: &[&str], title: &str, description: &str, prefix_suffix: &str) -> Self {
        let base = SubCommandBase::new(
            title,
            description,
            &format!("{COMMAND_PREFIX}{}{prefix_suffix}", names[0]),
        );
        Self {
            names: names.iter().map(|s| s.to_string()).collect(),
            base,
        }
    }

    /// `Register(subcommand, aliases)`.
    pub fn register(mut self, command: Arc<dyn SubCommand>, aliases: &[&str]) -> Self {
        self.base.register(command, aliases);
        self
    }

    pub fn base(&self) -> &SubCommandBase {
        &self.base
    }
}

impl SubCommand for RootCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }
}

impl Command for RootCommand {
    fn command_names(&self) -> Vec<String> {
        self.names.clone()
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("A ICommandV2 implementation should not be used as ICommand interface")
    }

    fn get_command_line_help(&self) -> String {
        format!("<{}>", self.base.supported_commands().join("||"))
    }

    fn get_command_help_text(&self) -> String {
        self.base.call_prefix.clone()
    }

    fn as_sub_command(&self) -> Option<&dyn SubCommand> {
        Some(self)
    }
}

/// `AStarCmd`.
pub fn astar_cmd() -> RootCommand {
    RootCommand::new(
        &["pathfind", "pf"],
        "[AStar]",
        "Root command to manage Path Findings",
        "",
    )
    // start searching for a path
    .register(
        Arc::new(AStarPathFindingSubCommand::default()),
        &["find", "go"],
    )
    // set the starting point of the path
    .register(Arc::new(AStarPointSubCommand::start()), &["start", "begin"])
    // set the end point of the path
    .register(Arc::new(AStarPointSubCommand::goal()), &["goal", "end"])
    // display the found path points on the terrain
    .register(Arc::new(AStarViewSubCommand::default()), &["view"])
}

/// `FeatureCmd` (registered as `feature`, `fset`, `fs`).
pub fn feature_cmd() -> RootCommand {
    RootCommand::new(
        &["feature"],
        "[Feature]",
        "Root command to manage Feature",
        "",
    )
    .register(Arc::new(FeatureSetSubCommand::default()), &["set", "s"])
    .register(Arc::new(FeatureCheckSubCommand::default()), &["check", "c"])
}

/// `FishFinderCmd`.
pub fn fish_finder_cmd() -> RootCommand {
    RootCommand::new(
        &["fishfinder"],
        "[FishFinder]",
        "Root command to manage FishFinder",
        "",
    )
    .register(Arc::new(FishFinderSetSubCommand::default()), &["set", "s"])
}

/// `TimeCmd`.
pub fn time_cmd() -> RootCommand {
    RootCommand::new(&["time"], "[Time]", "Root command to manage Time", "")
        .register(Arc::new(TimeSetSubCommand::default()), &["set", "s"])
}

/// `WorldCmd`.
pub fn world_cmd() -> RootCommand {
    RootCommand::new(&["world"], "[World]", "Root command to manage World", "")
        .register(Arc::new(WorldSetSubCommand::default()), &["set", "s"])
}
