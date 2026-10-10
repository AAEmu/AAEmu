//! Port of AAEmu.UnitTests/Game/Utils/Scripts/SubCommands/DoodadChainSubCommandTests.cs
//!
//! The C# mocks `ICommandV2.PreExecute(character, trigger, args, output)`; the Rust chain calls
//! the free [`pre_execute`] on the registered sub command, which (no parameters, no sub
//! commands, first argument not `help`) ends in `execute_args(trigger, args)`: the recording
//! sub command below records that call.

use std::sync::{Arc, Mutex};

use super::output::{CharacterMessageOutput, Color, RecordingCharacter};
use super::subcommand::{pre_execute, SubCommand, SubCommandBase};
use super::*;

/// The mocked `ICommandV2`: records (trigger, args) of every call that reaches it.
#[derive(Default)]
struct RecordingSubCommand {
    base: SubCommandBase,
    calls: Mutex<Vec<(String, Vec<String>)>>,
}

impl SubCommand for RecordingSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute_args(
        &self,
        _ctx: &CommandContext<'_>,
        trigger: &str,
        args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        self.calls
            .lock()
            .unwrap()
            .push((trigger.to_string(), args.to_vec()));
        Ok(())
    }
}

type Register<'a> = Vec<(Arc<dyn SubCommand>, Vec<&'a str>)>;

/// `TestCommand` / `SubTestCommand`: a `SubCommandBase` built from a register dictionary.
struct TestCommand {
    base: SubCommandBase,
}

impl TestCommand {
    /// `TestCommand(register)`: Title "Prefix", Description "Mock Command", CallPrefix
    /// "Help Message".
    fn new(register: Register<'_>) -> Self {
        let mut base = SubCommandBase::new("Prefix", "Mock Command", "Help Message");
        for (command, aliases) in register {
            base.register(command, &aliases);
        }
        Self { base }
    }

    /// `SubTestCommand(register)` (no title / description).
    fn sub(register: Register<'_>) -> Self {
        let mut base = SubCommandBase::default();
        for (command, aliases) in register {
            base.register(command, &aliases);
        }
        Self { base }
    }
}

impl SubCommand for TestCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }
}

fn run(cmd: &dyn SubCommand, trigger: &str, args: &[&str]) -> Arc<RecordingCharacter> {
    let character = RecordingCharacter::named("Fake");
    let ctx = CommandContext {
        server: None,
        character: character.clone(),
    };
    let mut out = CharacterMessageOutput::new(character.clone());
    let args: Vec<String> = args.iter().map(|s| s.to_string()).collect();
    pre_execute(cmd, &ctx, trigger, &args, &mut out);
    character
}

fn no_unexpected_error(c: &RecordingCharacter) {
    assert!(
        !c.messages().iter().any(|m| m.contains("Unexpected error")),
        "{:?}",
        c.messages()
    );
}

#[test]
fn pre_execute_when_chain_should_call_chain_sub_command() {
    let mock_sub_command = Arc::new(RecordingSubCommand::default());
    let command = TestCommand::new(vec![(mock_sub_command.clone(), vec!["sdf"])]);

    run(&command, "test", &["sdf", "123"]);

    assert_eq!(
        *mock_sub_command.calls.lock().unwrap(),
        vec![("sdf".to_string(), vec!["123".to_string()])]
    );
}

#[test]
fn pre_execute_when_chain_should_call_chain_sub_sub_command() {
    let mock_sub_sub_command = Arc::new(RecordingSubCommand::default());
    let sub_command = Arc::new(TestCommand::sub(vec![(
        mock_sub_sub_command.clone(),
        vec!["second"],
    )]));
    let command = TestCommand::new(vec![(sub_command, vec!["first"])]);

    run(
        &command,
        "test",
        &["first", "second", "parameter1second", "parameter2second"],
    );

    assert_eq!(
        *mock_sub_sub_command.calls.lock().unwrap(),
        vec![(
            "second".to_string(),
            vec![
                "parameter1second".to_string(),
                "parameter2second".to_string()
            ]
        )]
    );
}

#[test]
fn execute_when_only_command_should_not_throw_exception() {
    let test_command = TestCommand::new(vec![]);
    let c = run(&test_command, "doodad", &[]);
    no_unexpected_error(&c);
}

#[test]
fn execute_when_sending_help_should_return_help_text() {
    for number_of_supported_commands in [1usize, 2] {
        let names: Vec<String> = (0..number_of_supported_commands)
            .map(|i| format!("command{i}"))
            .collect();
        let mut supported: Register<'_> = Vec::new();
        for name in &names {
            supported.push((
                Arc::new(RecordingSubCommand::default()),
                vec![name.as_str()],
            ));
        }
        let test_command = TestCommand::new(supported);

        let c = run(&test_command, "test", &["help"]);

        // The C# assertions are commented out ("TODO: Fix these tests"); the help text the
        // C# `SendHelpMessage` produces is checked here.
        no_unexpected_error(&c);
        let green = |m: &str| Color::LAWN_GREEN.wrap(&format!("Prefix {m}"));
        assert_eq!(
            c.messages(),
            vec![
                green("Mock Command"),
                green(&format!("Supported subcommands: <{}>", names.join("||"))),
                green("For more details use /<command> <subcommand> help."),
            ]
        );
    }
}
