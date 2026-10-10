//! `Utils/Scripts/SubCommands/SubCommandBase` + `ICommandV2`: commands built from sub
//! commands and typed parameters.

use std::sync::Arc;

use futures::future::BoxFuture;

use super::output::{Color, MessageOutput};
use super::params::{ParameterResult, ParameterValue, Parameters, SubCommandParameter};
use super::CommandContext;
use crate::systems::social::chat::chat_type;

/// The `SubCommandBase` state (`Title`, `Description`, `CallPrefix`, `_subCommands`,
/// `_parameters`).
#[derive(Default)]
pub struct SubCommandBase {
    pub title: String,
    pub description: String,
    pub call_prefix: String,
    /// `_subCommands` (lower-cased alias -> command) in dictionary order.
    sub_commands: Vec<(String, Arc<dyn SubCommand>)>,
    parameters: Vec<Box<dyn SubCommandParameter>>,
}

impl std::fmt::Debug for SubCommandBase {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SubCommandBase")
            .field("title", &self.title)
            .field("sub_commands", &self.supported_commands())
            .field("parameters", &self.parameters.len())
            .finish()
    }
}

impl SubCommandBase {
    pub fn new(title: &str, description: &str, call_prefix: &str) -> Self {
        Self {
            title: title.into(),
            description: description.into(),
            call_prefix: call_prefix.into(),
            ..Self::default()
        }
    }

    /// `AddParameter(parameter)`: a required parameter cannot follow an optional one of the
    /// same kind (positional / prefixed).
    pub fn add_parameter(&mut self, parameter: Box<dyn SubCommandParameter>) -> anyhow::Result<()> {
        let b = parameter.base();
        if b.prefix.is_none()
            && b.is_required
            && !self.parameters.is_empty()
            && self
                .parameters
                .iter()
                .rev()
                .any(|x| !x.base().is_required && x.base().prefix.is_none())
        {
            anyhow::bail!("Cannot add a required parameter after an optional parameter");
        }
        if b.prefix.is_some()
            && b.is_required
            && !self.parameters.is_empty()
            && self
                .parameters
                .iter()
                .rev()
                .any(|x| !x.base().is_required && x.base().prefix.is_some())
        {
            anyhow::bail!(
                "Cannot add a required prefix parameter after an optional prefix parameter"
            );
        }
        self.parameters.push(parameter);
        Ok(())
    }

    /// `Register(command, params aliases)` (aliases are lower-cased; an existing alias keeps
    /// its dictionary slot).
    pub fn register(&mut self, command: Arc<dyn SubCommand>, aliases: &[&str]) {
        for alias in aliases.iter().map(|a| a.to_lowercase()) {
            match self.sub_commands.iter_mut().find(|(k, _)| *k == alias) {
                Some(slot) => slot.1 = command.clone(),
                None => self.sub_commands.push((alias, command.clone())),
            }
        }
    }

    /// `SupportedCommands`.
    pub fn supported_commands(&self) -> Vec<String> {
        self.sub_commands.iter().map(|(k, _)| k.clone()).collect()
    }

    fn sub_command(&self, name: &str) -> Option<Arc<dyn SubCommand>> {
        self.sub_commands
            .iter()
            .find(|(k, _)| k == name)
            .map(|(_, c)| c.clone())
    }

    pub fn has_parameters(&self) -> bool {
        !self.parameters.is_empty()
    }

    /// `LoadParametersValues(args)`.
    pub fn load_parameters_values(
        &self,
        args: &[String],
    ) -> anyhow::Result<Vec<(String, ParameterResult)>> {
        let mut values: Vec<(String, ParameterResult)> = Vec::new();
        let add = |values: &mut Vec<(String, ParameterResult)>, r: ParameterResult| {
            if values.iter().any(|(k, _)| *k == r.name) {
                anyhow::bail!(
                    "An item with the same key has already been added. Key: {}",
                    r.name
                );
            }
            values.push((r.name.clone(), r));
            Ok(())
        };

        let non_prefix: Vec<&String> = args.iter().filter(|a| !a.contains('=')).collect();
        for (count, p) in self
            .parameters
            .iter()
            .filter(|p| p.base().prefix.is_none())
            .enumerate()
        {
            let value = if count < non_prefix.len() {
                Some(p.load(non_prefix[count]))
            } else if p.base().is_required {
                Some(ParameterResult::new(
                    &p.base().name,
                    ParameterValue::Null,
                    Some(format!("Parameter {} is required", p.base().name)),
                ))
            } else {
                None
            };
            if let Some(v) = value {
                add(&mut values, v)?;
            }
        }

        // Prefixed parameters (anywhere in the arguments).
        let prefixed: Vec<&String> = args.iter().filter(|a| a.contains('=')).collect();
        for p in self.parameters.iter().filter(|p| p.base().prefix.is_some()) {
            let mut found: Option<&String> = None;
            let mut duplicated = false;
            for a in &prefixed {
                if p.base().match_prefix(a) {
                    duplicated = found.is_some();
                    found = Some(a);
                }
            }
            if let Some(arg) = found {
                let value = if duplicated {
                    ParameterResult::new(
                        &p.base().name,
                        ParameterValue::Null,
                        Some(format!(
                            "Parameter prefix {} is duplicated",
                            p.base().prefix.as_deref().unwrap_or_default()
                        )),
                    )
                } else {
                    p.load(arg)
                };
                add(&mut values, value)?;
            }
        }

        // Optional parameters with a default value.
        for p in &self.parameters {
            let b = p.base();
            if b.is_required || values.iter().any(|(k, _)| *k == b.name) {
                continue;
            }
            if let Some(d) = &b.default_value {
                add(&mut values, ParameterResult::new(&b.name, d.clone(), None))?;
            }
        }
        Ok(values)
    }

    /// `PreValidate(messageOutput, parameters)`.
    pub fn pre_validate<'a>(
        &self,
        out: &mut dyn MessageOutput,
        results: impl IntoIterator<Item = &'a ParameterResult>,
    ) -> bool {
        let mut valid = true;
        for r in results {
            if !r.is_valid() {
                valid = false;
                self.send_color_message(
                    out,
                    Color::RED,
                    r.invalid_message.as_deref().unwrap_or(""),
                );
            }
        }
        valid
    }

    /// The default `SendHelpMessage`.
    pub fn send_help_message(&self, out: &mut dyn MessageOutput) {
        self.send_color_message(out, Color::LAWN_GREEN, &self.description.clone());
        if !self.parameters.is_empty() {
            let example = self.get_call_example();
            self.send_color_message(out, Color::LAWN_GREEN, &example);
        }
        let supported = self.supported_commands();
        if !supported.is_empty() {
            self.send_color_message(
                out,
                Color::LAWN_GREEN,
                &format!("Supported subcommands: <{}>", supported.join("||")),
            );
            self.send_color_message(
                out,
                Color::LAWN_GREEN,
                "For more details use /<command> <subcommand> help.",
            );
        }
    }

    /// `GetCallExample()`: positional before prefixed, required before optional.
    pub fn get_call_example(&self) -> String {
        let mut s = self.call_prefix.clone();
        let mut ordered: Vec<&Box<dyn SubCommandParameter>> = self.parameters.iter().collect();
        ordered.sort_by_key(|p| (p.base().prefix.is_some(), !p.base().is_required));
        for p in ordered {
            if p.base().is_required {
                s.push_str(&format!(" <{}>", p.call_example()));
            } else {
                s.push_str(&format!(" [{}]", p.call_example()));
            }
        }
        s
    }

    /// `SendMessage(messageOutput, message)`: `"{Title} {message}"`.
    pub fn send_message(&self, out: &mut dyn MessageOutput, message: &str) {
        out.send_message(&format!("{} {message}", self.title));
    }

    /// `SendMessage(target, messageOutput, message)`.
    pub fn send_message_to(
        &self,
        target: &dyn super::output::ICharacter,
        out: &mut dyn MessageOutput,
        message: &str,
    ) {
        out.send_message_to(target, &format!("{} {message}", self.title));
    }

    /// `SendColorMessage(messageOutput, color, message)`.
    pub fn send_color_message(&self, out: &mut dyn MessageOutput, color: Color, message: &str) {
        out.send_message_typed(
            chat_type::SYSTEM,
            &format!("{} {message}", self.title),
            Some(color),
        );
    }
}

/// `GetOptionalArgumentValue(args, name, defaultValue)` (string).
pub fn optional_argument_string(args: &[String], name: &str, default: &str) -> String {
    match args.iter().find(|a| a.starts_with(&format!("{name}="))) {
        None => default.to_string(),
        Some(a) => a.split('=').nth(1).unwrap_or("").trim().to_string(),
    }
}

/// `GetOptionalArgumentValue(args, name, defaultValue)` (float).
pub fn optional_argument_f32(args: &[String], name: &str, default: f32) -> anyhow::Result<f32> {
    match args.iter().find(|a| a.starts_with(&format!("{name}="))) {
        None => Ok(default),
        Some(a) => a
            .split('=')
            .nth(1)
            .unwrap_or("")
            .trim()
            .parse()
            .map_err(|_| anyhow::anyhow!("Invalid value [{a}] for parameter {name} ")),
    }
}

/// `GetOptionalParameterValue<T>(parameters, name, default)`.
pub fn optional_parameter<T>(
    parameters: &Parameters,
    name: &str,
    default: T,
    cast: impl Fn(&ParameterValue) -> anyhow::Result<T>,
) -> anyhow::Result<T> {
    match parameters.get(name) {
        None => Ok(default),
        Some(v) => cast(v),
    }
}

/// `SubCommandBase` virtuals + `ICommandV2`.
pub trait SubCommand: Send + Sync {
    fn base(&self) -> &SubCommandBase;

    /// `Execute(character, triggerArgument, string[] args, messageOutput)` (default: help).
    fn execute_args(
        &self,
        _ctx: &CommandContext<'_>,
        _trigger: &str,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        self.send_help_message(out);
        Ok(())
    }

    /// `Execute(character, triggerArgument, IDictionary<string, ParameterValue>, ...)`
    /// (default: help).
    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _trigger: &str,
        _parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        self.send_help_message(out);
        Ok(())
    }

    /// [`SubCommand::execute_args`] for sub commands whose body awaits an async Rust API.
    fn execute_args_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        trigger: &'a str,
        args: &'a [String],
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move { self.execute_args(ctx, trigger, args, out) })
    }

    /// [`SubCommand::execute`] for sub commands whose body awaits an async Rust API.
    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        trigger: &'a str,
        parameters: &'a Parameters,
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move { self.execute(ctx, trigger, parameters, out) })
    }

    /// `SendHelpMessage(messageOutput)` (virtual).
    fn send_help_message(&self, out: &mut dyn MessageOutput) {
        self.base().send_help_message(out);
    }

    /// `ICommandV2.Description`.
    fn description(&self) -> &str {
        &self.base().description
    }

    /// `ICommandV2.CallPrefix`.
    fn call_prefix(&self) -> &str {
        &self.base().call_prefix
    }
}

/// `SubCommandBase.PreExecute(character, triggerArgument, args, messageOutput)`.
pub fn pre_execute(
    cmd: &dyn SubCommand,
    ctx: &CommandContext<'_>,
    trigger: &str,
    args: &[String],
    out: &mut dyn MessageOutput,
) {
    let base = cmd.base();
    let result = (|| -> anyhow::Result<()> {
        let Some(first) = args.first() else {
            return cmd.execute_args(ctx, trigger, args, out);
        };
        if first.eq_ignore_ascii_case("help") {
            cmd.send_help_message(out);
        } else if let Some(sub) = base.sub_command(first) {
            pre_execute(sub.as_ref(), ctx, first, &args[1..], out);
        } else if base.has_parameters() {
            let results = base.load_parameters_values(args)?;
            if base.pre_validate(out, results.iter().map(|(_, r)| r)) {
                let parameters =
                    Parameters(results.into_iter().map(|(k, r)| (k, r.value)).collect());
                cmd.execute(ctx, trigger, &parameters, out)?;
            }
        } else {
            // Backwards compatibility with non parameter subcommands.
            cmd.execute_args(ctx, trigger, args, out)?;
        }
        Ok(())
    })();
    if let Err(e) = result {
        base.send_color_message(out, Color::RED, &format!("Unexpected error: {e}"));
        tracing::error!("{e:?}");
    }
}

/// [`pre_execute`] running the async forms of the sub command bodies (what
/// `CommandManager.Handle` uses).
pub fn pre_execute_async<'a>(
    cmd: &'a dyn SubCommand,
    ctx: &'a CommandContext<'a>,
    trigger: &'a str,
    args: &'a [String],
    out: &'a mut dyn MessageOutput,
) -> BoxFuture<'a, ()> {
    Box::pin(async move {
        let base = cmd.base();
        let result: anyhow::Result<()> = async {
            let Some(first) = args.first() else {
                return cmd.execute_args_async(ctx, trigger, args, out).await;
            };
            if first.eq_ignore_ascii_case("help") {
                cmd.send_help_message(out);
            } else if let Some(sub) = base.sub_command(first) {
                pre_execute_async(sub.as_ref(), ctx, first, &args[1..], out).await;
            } else if base.has_parameters() {
                let results = base.load_parameters_values(args)?;
                if base.pre_validate(out, results.iter().map(|(_, r)| r)) {
                    let parameters =
                        Parameters(results.into_iter().map(|(k, r)| (k, r.value)).collect());
                    cmd.execute_async(ctx, trigger, &parameters, out).await?;
                }
            } else {
                // Backwards compatibility with non parameter subcommands.
                cmd.execute_args_async(ctx, trigger, args, out).await?;
            }
            Ok(())
        }
        .await;
        if let Err(e) = result {
            base.send_color_message(out, Color::RED, &format!("Unexpected error: {e}"));
            tracing::error!("{e:?}");
        }
    })
}
