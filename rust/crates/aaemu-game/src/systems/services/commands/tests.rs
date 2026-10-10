//! `CommandManager` behaviour and ports of `SubCommandBaseTests.cs` (with `SubCommandFake`).

use std::sync::{Arc, Mutex};

use super::output::{CharacterMessageOutput, Color, RecordingCharacter};
use super::params::{
    NumericSubCommandParameter, ParameterValue, Parameters, StringSubCommandParameter,
    SubCommandParameter,
};
use super::subcommand::{pre_execute, SubCommand, SubCommandBase};
use super::*;

/// `SubCommandFake`.
struct SubCommandFake {
    base: SubCommandBase,
    executed: Mutex<Option<Parameters>>,
}

impl SubCommandFake {
    fn new(parameters: Vec<Box<dyn SubCommandParameter>>) -> Self {
        let mut base = SubCommandBase::new(
            "[Test]",
            "Test Subcommand",
            &format!("{COMMAND_PREFIX}test"),
        );
        for p in parameters {
            base.add_parameter(p).unwrap();
        }
        Self {
            base,
            executed: Mutex::new(None),
        }
    }

    fn executed(&self) -> Option<Parameters> {
        self.executed.lock().unwrap().clone()
    }
}

impl SubCommand for SubCommandFake {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        *self.executed.lock().unwrap() = Some(parameters.clone());
        Ok(())
    }
}

fn run(cmd: &SubCommandFake, args: &[&str]) -> Arc<RecordingCharacter> {
    let character = RecordingCharacter::named("Tester");
    let ctx = CommandContext {
        server: None,
        character: character.clone(),
    };
    let mut out = CharacterMessageOutput::new(character.clone());
    let args: Vec<String> = args.iter().map(|s| s.to_string()).collect();
    pre_execute(cmd, &ctx, "", &args, &mut out);
    character
}

fn strings(names: &str, required: bool) -> Vec<Box<dyn SubCommandParameter>> {
    names
        .split(',')
        .map(|n| {
            Box::new(StringSubCommandParameter::new(n, None, required, &[]))
                as Box<dyn SubCommandParameter>
        })
        .collect()
}

#[test]
fn pre_validate_when_required_string_parameters_are_not_met() {
    for (required, args) in [
        ("param1,param2", vec!["test"]),
        ("param1,param2,param3,param4", vec!["test"]),
        (
            "param1,param2,param3,param4",
            vec!["test", "test2", "test3"],
        ),
    ] {
        let cmd = SubCommandFake::new(strings(required, true));
        let c = run(&cmd, &args);
        assert!(cmd.executed().is_none());
        let n = required.split(',').count();
        let expected: Vec<String> = required
            .split(',')
            .skip(args.len())
            .map(|p| Color::RED.wrap(&format!("[Test] Parameter {p} is required")))
            .collect();
        assert_eq!(c.messages(), expected, "{n}");
    }
}

#[test]
fn pre_validate_when_duplicated_prefix_parameters() {
    for (prefixes, args) in [
        ("required-prefix-x", vec!["x=test", "x=test2duplicate"]),
        ("optional-prefix-y", vec!["y=test", "y=test2duplicate"]),
        (
            "optional-prefix-name",
            vec!["y=test", "y=test2duplicate", "name=ok", "name=duplicate"],
        ),
    ] {
        let params: Vec<Box<dyn SubCommandParameter>> = prefixes
            .split(',')
            .map(|p| {
                let prefix = p.rsplit("prefix-").next().unwrap();
                Box::new(StringSubCommandParameter::with_prefix(
                    p,
                    None,
                    p.contains("required"),
                    Some(prefix),
                )) as Box<dyn SubCommandParameter>
            })
            .collect();
        let cmd = SubCommandFake::new(params);
        let c = run(&cmd, &args);
        assert!(cmd.executed().is_none());
        let prefix = prefixes.rsplit("prefix-").next().unwrap();
        assert_eq!(
            c.messages(),
            vec![Color::RED.wrap(&format!("[Test] Parameter prefix {prefix} is duplicated"))]
        );
    }
}

#[test]
fn pre_validate_when_required_string_parameters_are_met() {
    for (required, args) in [
        ("param1,param2", vec!["test", "test2"]),
        (
            "param1,param2,param3,param4",
            vec!["test", "test2", "test3", "test4"],
        ),
    ] {
        let cmd = SubCommandFake::new(strings(required, true));
        let c = run(&cmd, &args);
        let p = cmd.executed().expect("executed");
        assert_eq!(p.len(), args.len());
        for ((k, v), (name, arg)) in p.iter().zip(required.split(',').zip(&args)) {
            assert_eq!(k, name);
            assert_eq!(v, &ParameterValue::from(*arg));
        }
        assert!(c.messages().is_empty());
    }
}

#[test]
fn load_parameters_values_when_optional_parameters_are_not_present() {
    for (required, optional, args) in [
        ("param1", "param2", vec!["test"]),
        ("param1,param2", "param3,param4", vec!["test", "test2"]),
        (
            "param1,param2",
            "param3,param4",
            vec!["test", "test2", "test3"],
        ),
        (
            "param1,param2",
            "param3,param4",
            vec!["test", "test2", "test3", "test4"],
        ),
        (
            "param1,param2",
            "param3,param4",
            vec!["test", "test2", "test3", "test4", "test5"],
        ),
        (
            "param1,param2",
            "param3,param4,param5",
            vec!["test", "test2", "test3", "test4", "test5"],
        ),
        (
            "param1,param2",
            "param3,param4,param5",
            vec!["test", "test2", "test3", "test4", "test5", "test6", "test7"],
        ),
    ] {
        let mut params = strings(required, true);
        params.extend(strings(optional, false));
        let names: Vec<&str> = required.split(',').chain(optional.split(',')).collect();
        let cmd = SubCommandFake::new(params);
        let c = run(&cmd, &args);
        let p = cmd.executed().expect("executed");
        let expected = args.len().min(names.len());
        assert_eq!(p.len(), expected);
        for (i, (k, v)) in p.iter().enumerate() {
            assert_eq!(k, names[i]);
            assert_eq!(v, &ParameterValue::from(args[i]));
        }
        assert!(c.messages().is_empty());
    }
}

#[test]
fn ranged_string_parameters() {
    let cmd = SubCommandFake::new(vec![Box::new(StringSubCommandParameter::new(
        "param1",
        Some("parameter 1"),
        true,
        &["valid1", "valid2"],
    ))]);
    let c = run(&cmd, &["test"]);
    assert!(cmd.executed().is_none());
    assert_eq!(
        c.messages(),
        vec![Color::RED.wrap("[Test] Parameter [parameter 1] only accepts: valid1||valid2")]
    );
    let cmd = SubCommandFake::new(vec![Box::new(StringSubCommandParameter::new(
        "param1",
        Some("parameter 1"),
        true,
        &["test", "test2", "test3"],
    ))]);
    let c = run(&cmd, &["test"]);
    let p = cmd.executed().unwrap();
    assert_eq!(p.len(), 1);
    assert_eq!(p.require("param1").unwrap(), &ParameterValue::from("test"));
    assert!(c.messages().is_empty());
}

#[test]
fn mixed_non_prefix_and_any_order_prefix() {
    for args in [
        vec![
            "x=test",
            "firstRequired",
            "SecondOptional",
            "shouldIgnoreMe",
        ],
        vec![
            "firstRequired",
            "x=test",
            "SecondOptional",
            "shouldIgnoreMe",
            "shouldIgnoreMe",
        ],
        vec![
            "firstRequired",
            "SecondOptional",
            "x=test",
            "shouldIgnoreMe",
            "shouldIgnoreMe",
            "y=z",
        ],
        vec![
            "firstRequired",
            "SecondOptional",
            "x=test",
            "shouldIgnoreMe",
            "shouldIgnoreMe",
            "y=z",
            "ignoreMe",
        ],
    ] {
        let mut params = strings("req1", true);
        params.extend(strings("opt2", false));
        params.push(Box::new(StringSubCommandParameter::with_prefix(
            "required-prefix-x",
            None,
            true,
            Some("x"),
        )));
        let cmd = SubCommandFake::new(params);
        let c = run(&cmd, &args);
        let p = cmd.executed().expect("executed");
        assert_eq!(p.len(), 3);
        assert_eq!(
            p.require("req1").unwrap(),
            &ParameterValue::from("firstRequired")
        );
        assert_eq!(
            p.require("opt2").unwrap(),
            &ParameterValue::from("SecondOptional")
        );
        assert_eq!(
            p.require("required-prefix-x").unwrap(),
            &ParameterValue::from("test")
        );
        assert!(!p.iter().any(|(_, v)| v.to_string() == "z"));
        assert!(c.messages().is_empty());
    }
}

/// `GetParameter(pattern)` of the C# tests.
fn parameter(pattern: &str) -> Box<dyn SubCommandParameter> {
    let parts: Vec<&str> = pattern.split('-').collect();
    let required = parts[0] == "required";
    let prefix = parts
        .iter()
        .position(|p| *p == "prefix")
        .map(|i| parts[i + 1]);
    let default = parts.iter().position(|p| *p == "default").map(|i| {
        let t = parts[i + 1];
        if let Some(v) = t.strip_prefix("(int)") {
            ParameterValue::Int32(v.parse().unwrap())
        } else if let Some(v) = t.strip_prefix("(float)") {
            ParameterValue::Single(v.parse().unwrap())
        } else if let Some(v) = t.strip_prefix("(uint)") {
            ParameterValue::UInt32(v.parse().unwrap())
        } else if let Some(v) = t.strip_prefix("(long)") {
            ParameterValue::Int64(v.parse().unwrap())
        } else if let Some(v) = t.strip_prefix("(byte)") {
            ParameterValue::Byte(v.parse().unwrap())
        } else {
            ParameterValue::from(t)
        }
    });
    let mut p: Box<dyn SubCommandParameter> = match parts[1] {
        "int" => Box::new(NumericSubCommandParameter::<i32>::with_prefix(
            pattern, None, required, prefix,
        )),
        "uint" => Box::new(NumericSubCommandParameter::<u32>::with_prefix(
            pattern, None, required, prefix,
        )),
        "long" => Box::new(NumericSubCommandParameter::<i64>::with_prefix(
            pattern, None, required, prefix,
        )),
        "float" => Box::new(NumericSubCommandParameter::<f32>::with_prefix(
            pattern, None, required, prefix,
        )),
        "byte" => Box::new(NumericSubCommandParameter::<u8>::with_prefix(
            pattern, None, required, prefix,
        )),
        _ => Box::new(StringSubCommandParameter::with_prefix(
            pattern, None, required, prefix,
        )),
    };
    p.base_mut().default_value = default;
    p
}

#[test]
fn load_parameters_values_mixed_parameters() {
    for (pattern, name, expected, args) in [
        (
            "required-long-prefix-w,optional-int-prefix-x,required-float,required-string",
            "required-float",
            ParameterValue::Single(-13.23),
            vec!["x=12", "w=100", "-13.23", "requiredstring", "extrastring"],
        ),
        (
            "required-long-prefix-w,optional-int-prefix-x,required-float,required-string",
            "required-float",
            ParameterValue::Single(123.54),
            vec!["w=100", "123.54", "requiredstring", "extrastring"],
        ),
        (
            "required-float-prefix-yaw,optional-int-prefix-x,required-byte,optional-string",
            "required-float-prefix-yaw",
            ParameterValue::Single(185.4323),
            vec!["yaw=185.4323", "200"],
        ),
    ] {
        let cmd = SubCommandFake::new(pattern.split(',').map(parameter).collect());
        run(&cmd, &args);
        let p = cmd.executed().expect("executed");
        for req in pattern.split(',').filter(|x| x.starts_with("required")) {
            assert!(p.contains_key(req), "{req}");
        }
        assert_eq!(p.require(name).unwrap(), &expected);
    }
}

#[test]
fn send_help_message_mixed_parameters() {
    for (pattern, expected) in [
        (
            "required-long-prefix-w,optional-int-prefix-x,required-float,required-string",
            "[Test] /test <required-float> <required-string> <required-long-prefix-w> [optional-int-prefix-x]",
        ),
        (
            "required-float-prefix-yaw,optional-int-prefix-x,required-byte,optional-string",
            "[Test] /test <required-byte> [optional-string] <required-float-prefix-yaw> [optional-int-prefix-x]",
        ),
    ] {
        let cmd = SubCommandFake::new(pattern.split(',').map(parameter).collect());
        let c = RecordingCharacter::named("x");
        let mut out = CharacterMessageOutput::new(c.clone());
        cmd.send_help_message(&mut out);
        let hits = c.messages().iter().filter(|m| m.contains(expected)).count();
        assert_eq!(hits, 1, "{:?}", c.messages());
    }
}

#[test]
fn send_help_message_string_valid_values() {
    for (expected, values) in [
        ("[Test] /test <a||b||c>", vec!["a", "b", "c"]),
        ("[Test] /test <a||b>", vec!["a", "b"]),
        ("[Test] /test <a>", vec!["a"]),
        ("[Test] /test <test display name>", vec![]),
    ] {
        let cmd = SubCommandFake::new(vec![Box::new(StringSubCommandParameter::new(
            "test",
            Some("test display name"),
            true,
            &values,
        ))]);
        let c = RecordingCharacter::named("x");
        let mut out = CharacterMessageOutput::new(c.clone());
        cmd.send_help_message(&mut out);
        assert_eq!(
            c.messages().iter().filter(|m| m.contains(expected)).count(),
            1
        );
    }
}

#[test]
fn load_parameter_when_optional_default_parameters_are_not_provided() {
    for (pattern, name, expected, args) in [
        (
            "required-long-prefix-w,optional-int-prefix-x-default-(int)10,required-float,required-string",
            "optional-int-prefix-x-default-(int)10",
            ParameterValue::Int32(10),
            vec!["w=100", "-13.23", "requiredstring", "extrastring"],
        ),
        (
            "optional-int-prefix-x-default-(float)50.93,required-string,optional-string-default-ok",
            "optional-string-default-ok",
            ParameterValue::from("ok"),
            vec!["anything"],
        ),
        (
            "optional-int-prefix-x-default-(float)50.93,required-string,optional-string-default-ok",
            "optional-int-prefix-x-default-(float)50.93",
            ParameterValue::Single(50.93),
            vec!["anything"],
        ),
        (
            "optional-int-prefix-x-default-(uint)50,required-string,optional-string-default-ok",
            "optional-int-prefix-x-default-(uint)50",
            ParameterValue::UInt32(50),
            vec!["anything"],
        ),
        (
            "optional-int-prefix-x-default-(byte)250,required-string,optional-string-default-ok",
            "optional-int-prefix-x-default-(byte)250",
            ParameterValue::Byte(250),
            vec!["anything"],
        ),
        (
            "optional-int-prefix-x-default-(long)1000000,required-string,optional-string-default-ok",
            "optional-int-prefix-x-default-(long)1000000",
            ParameterValue::Int64(1_000_000),
            vec!["anything"],
        ),
    ] {
        let cmd = SubCommandFake::new(pattern.split(',').map(parameter).collect());
        run(&cmd, &args);
        let p = cmd.executed().expect("executed");
        assert_eq!(p.require(name).unwrap(), &expected);
    }
}

#[test]
fn add_parameter_order_rules() {
    let mut b = SubCommandBase::new("[T]", "d", "/t");
    b.add_parameter(Box::new(StringSubCommandParameter::new(
        "a",
        None,
        false,
        &[],
    )))
    .unwrap();
    assert!(b
        .add_parameter(Box::new(StringSubCommandParameter::new(
            "b",
            None,
            true,
            &[]
        )))
        .is_err());
    // Prefixed parameters are checked separately.
    b.add_parameter(Box::new(StringSubCommandParameter::with_prefix(
        "c",
        None,
        false,
        Some("c"),
    )))
    .unwrap();
    assert!(b
        .add_parameter(Box::new(StringSubCommandParameter::with_prefix(
            "d",
            None,
            true,
            Some("d")
        )))
        .is_err());
}

#[test]
fn sub_command_routing_and_help() {
    struct Root {
        base: SubCommandBase,
    }
    impl SubCommand for Root {
        fn base(&self) -> &SubCommandBase {
            &self.base
        }
    }
    let leaf = Arc::new(SubCommandFake::new(strings("v", true)));
    let mut base = SubCommandBase::new("[Root]", "Root command", "/root");
    base.register(leaf.clone(), &["Leaf", "l"]);
    let root = Root { base };
    let c = RecordingCharacter::named("x");
    let ctx = CommandContext {
        server: None,
        character: c.clone(),
    };
    let mut out = CharacterMessageOutput::new(c.clone());
    pre_execute(&root, &ctx, "root", &["l".into(), "value".into()], &mut out);
    assert_eq!(
        leaf.executed().unwrap().require("v").unwrap(),
        &ParameterValue::from("value")
    );
    // Aliases are registered lower-cased and looked up as typed.
    pre_execute(&root, &ctx, "root", &["HELP".into()], &mut out);
    let green = |m: &str| Color::LAWN_GREEN.wrap(&format!("[Root] {m}"));
    assert_eq!(
        c.messages(),
        vec![
            green("Root command"),
            green("Supported subcommands: <leaf||l>"),
            green("For more details use /<command> <subcommand> help."),
        ]
    );
}

#[test]
fn split_command_string_matches_the_dotnet_regex() {
    assert_eq!(split_command_string("a b  c"), ["a", "b", "c"]);
    assert_eq!(
        split_command_string("say \"hello world\" x"),
        ["say", "hello world", "x"]
    );
    assert_eq!(split_command_string("a\"b c\"d"), ["a", "b c", "d"]);
    // Between two quoted parts the separator is itself a "quoted" match.
    assert_eq!(split_command_string("\"a\" \"b\""), ["a", " ", "b"]);
    assert_eq!(split_command_string("x \"\" y"), ["x", "", "y"]);
    assert!(split_command_string("").is_empty());
}

/// A plain `ICommand` for the manager tests.
struct EchoCommand {
    fail: bool,
}

impl Command for EchoCommand {
    fn command_names(&self) -> Vec<String> {
        vec!["echo".into(), "e".into()]
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if self.fail {
            anyhow::bail!("broken");
        }
        out.send_message(&args.join("|"));
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<text>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Echoes".into()
    }
}

#[test]
fn command_manager_handle() {
    let m = CommandManager::default();
    let c = RecordingCharacter::named("GM");
    let ctx = CommandContext {
        server: None,
        character: c.clone(),
    };
    // No commands loaded.
    let (handled, out) = m.handle_with_level(&ctx, 0, "echo hi");
    assert!(!handled);
    assert_eq!(out.messages(), ["[Error] No commands available."]);
    let (handled, out) = m.handle_with_level(&ctx, 100, "echo hi");
    assert!(!handled);
    assert!(out.messages()[0].starts_with("|cFFFF0000[Error] No commands have been loaded"));
    // Forced reload with a loader.
    m.set_loader(Arc::new(|m: &CommandManager| {
        m.register_names(&["echo", "e"], Arc::new(EchoCommand { fail: false }));
        true
    }));
    let (handled, _) = m.handle_with_level(&ctx, 100, "scripts reload force");
    assert!(handled);
    assert_eq!(
        c.messages().last().unwrap(),
        "[Force Scripts Reload] Success"
    );
    assert_eq!(m.get_command_keys(), ["echo"]);
    // Access levels: unknown commands need 100.
    let (handled, out) = m.handle_with_level(&ctx, 50, "echo hi");
    assert!(handled);
    assert_eq!(out.messages(), ["|cFFFF0000Insufficient privileges.|r"]);
    m.access_levels.load(&[("echo".into(), 0)]);
    let (handled, out) = m.handle_with_level(&ctx, 0, "E \"a b\" c||d");
    assert!(handled);
    assert_eq!(out.messages(), ["a b|c|d"]);
    // Unknown command: not handled (the text goes to chat).
    let (handled, out) = m.handle_with_level(&ctx, 100, "nope");
    assert!(!handled);
    assert!(out.messages().is_empty());
    // Exceptions go to the character in red.
    m.register("boom", Arc::new(EchoCommand { fail: true }));
    m.access_levels.load(&[("boom".into(), 0)]);
    let (handled, _) = m.handle_with_level(&ctx, 0, "boom");
    assert!(handled);
    assert_eq!(c.messages().last().unwrap(), "broken");
    assert_eq!(
        c.received.lock().unwrap().last().unwrap().2,
        Some(Color::RED)
    );
    assert!(m.get_command_interface_by_name("E").is_some());
    assert_eq!(m.get_command_name_base("e"), "echo");
    assert_eq!(m.get_command_name_base("zz"), "");
    assert_eq!(m.un_alias_command_name("E"), "echo");
}

#[test]
fn help_texts() {
    let c = RecordingCharacter::named("x");
    let mut out = CharacterMessageOutput::new(c);
    let cmd = EchoCommand { fail: false };
    CommandManager::send_default_help_text(&cmd, &mut out);
    CommandManager::send_error_text(&cmd, &mut out, "bad");
    CommandManager::send_normal_text(&cmd, &mut out, "ok");
    assert_eq!(
        out.messages(),
        [
            "Help for |cFFFFFFFF/echo|r |cFFEEEEAA<text>|r\n|cFF888888Echoes|r",
            "|cFFFFFFFF[echo]|r |cFFFF0000bad|r",
            "[echo] ok",
        ]
    );
}

#[test]
fn access_level_manager() {
    // Port of `AccessLevelManagerTests.cs`: configured levels, default 100, exact names.
    let m = AccessLevelManager::default();
    assert_eq!(m.get_level("help"), 100);
    m.load(&[("help".into(), 0), ("kick".into(), 50)]);
    assert_eq!(m.len(), 2);
    assert_eq!(m.get_level("help"), 0);
    assert_eq!(m.get_level("kick"), 50);
    assert_eq!(m.get_level("Kick"), 100);
}

#[tokio::test]
async fn world_set_and_time_sub_commands() {
    use super::sub::{TimeSetSubCommand, WorldSetSubCommand};
    let server = crate::test_support::test_server();
    let c = RecordingCharacter::named("GM");
    let ctx = CommandContext {
        server: Some(&server),
        character: c.clone(),
    };
    let mut out = CharacterMessageOutput::new(c.clone());
    let world = WorldSetSubCommand::default();
    pre_execute(
        &world,
        &ctx,
        "set",
        &["exprate".into(), "2.5".into()],
        &mut out,
    );
    assert_eq!(server.config.world.exp_rate, 2.5);
    pre_execute(&world, &ctx, "set", &["er".into(), "0.5".into()], &mut out);
    assert_eq!(server.config.world.exp_rate, 2.5);
    pre_execute(&world, &ctx, "set", &["gr".into(), "3".into()], &mut out);
    assert_eq!(server.systems.npcs.doodads.growth_rate(), 3.0);
    pre_execute(&world, &ctx, "set", &["lr".into(), "4".into()], &mut out);
    pre_execute(
        &world,
        &ctx,
        "set",
        &["motd".into(), "Hello".into()],
        &mut out,
    );
    assert_eq!(server.config.world.motd, "Hello");
    assert_eq!(
        out.messages(),
        [
            "[World Set ExpRate] Set ExpRate 2.5",
            &Color::CORAL.wrap(
                "[World Set ExpRate] Exp Rate = 0.5 must be at least 1.0 and no more than 1000.0"
            ),
            "[World Set GrowthRate] Set GrowthRate 3",
            // C#: the loot rate command reports "Set GrowthRate".
            "[World Set LootRate] Set GrowthRate 4",
            "[World Set MOTD] Set MOTD Hello",
        ]
    );
    let time = TimeSetSubCommand::default();
    let mut out = CharacterMessageOutput::new(c.clone());
    pre_execute(&time, &ctx, "set", &["6".into(), "30".into()], &mut out);
    assert_eq!(server.time.get_time(), 6.5);
    assert_eq!(
        out.messages(),
        ["[Time Information] Changed game time 12.00 -> 6.50 (6h30m)"]
    );
}
