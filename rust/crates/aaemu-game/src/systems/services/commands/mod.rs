//! The chat-command infrastructure: `Core/Managers/CommandManager`, `AccessLevelManager`,
//! `Models/Game/ICommand`, `Utils/Scripts/**` (`IMessageOutput`, `CharacterMessageOutput`,
//! `ScriptReflector` / `ScriptCompiler` -> [`CommandManager::reload`], the sub-command and
//! parameter framework) and the sub commands that live in `Utils/Scripts/SubCommands`.
//!
//! The GM commands themselves (`Scripts/Commands/**`) are a later phase: they implement
//! [`Command`] (optionally through [`subcommand::SubCommand`]) and are registered by the
//! loader installed with [`CommandManager::set_loader`] (the C# script reflection / compile
//! step). With no commands registered, `Handle` answers exactly like the C# server without
//! loaded scripts.

pub mod output;
pub mod params;
pub mod sub;
pub mod subcommand;

use std::collections::HashMap;
use std::sync::{Arc, RwLock};

use self::output::{CharacterMessageOutput, Color, ICharacter, MessageOutput, PlayerCharacter};
use self::subcommand::SubCommand;
use crate::server::GameServer;
use crate::systems::social::chat::chat_type;
use crate::world::PlayerRef;

/// `CommandManager.CommandPrefix`.
pub const COMMAND_PREFIX: &str = "/";

/// What a command runs with: the server (`None` in unit tests) and the calling character.
pub struct CommandContext<'a> {
    pub server: Option<&'a GameServer>,
    pub character: Arc<dyn ICharacter>,
}

impl CommandContext<'_> {
    /// The server, or the error a C# singleton access would raise without one.
    pub fn server(&self) -> anyhow::Result<&GameServer> {
        self.server
            .ok_or_else(|| anyhow::anyhow!("Object reference not set to an instance of an object."))
    }

    /// The calling player (commands that need the world object).
    pub fn player(&self) -> anyhow::Result<PlayerRef> {
        self.character
            .player()
            .ok_or_else(|| anyhow::anyhow!("Object reference not set to an instance of an object."))
    }
}

/// `ICommand` (+ `ICommandV2` through [`Command::as_sub_command`]).
pub trait Command: Send + Sync {
    /// `CommandNames` (the first is the command, the others are aliases).
    fn command_names(&self) -> Vec<String>;
    /// `Execute(character, args, messageOutput)`; an `Err` is a thrown exception.
    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()>;
    /// `GetCommandLineHelp()`.
    fn get_command_line_help(&self) -> String;
    /// `GetCommandHelpText()`.
    fn get_command_help_text(&self) -> String;
    /// `command is ICommandV2 subcommand`.
    fn as_sub_command(&self) -> Option<&dyn SubCommand> {
        None
    }
}

/// `AccessLevelManager`: command name -> required access level (`AccessLevel` section).
#[derive(Debug, Default)]
pub struct AccessLevelManager {
    /// `CMD` (in load order).
    commands: RwLock<Vec<(String, i32)>>,
}

impl AccessLevelManager {
    /// `Load()`.
    pub fn load(&self, levels: &[(String, i32)]) {
        tracing::info!("Loading CommandAccessLevels...");
        let mut cmds = self.commands.write().expect("access levels poisoned");
        cmds.extend(levels.iter().cloned());
        tracing::info!("Loaded {} CommandAccessLevels", cmds.len());
    }

    /// `GetLevel(commandStr)`: the first exact (case-sensitive) match, else 100.
    pub fn get_level(&self, command: &str) -> i32 {
        self.commands
            .read()
            .expect("access levels poisoned")
            .iter()
            .find(|(n, _)| n == command)
            .map_or(100, |(_, l)| *l)
    }

    pub fn len(&self) -> usize {
        self.commands.read().map(|c| c.len()).unwrap_or(0)
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

/// The script load step (`ScriptReflector.Reflect` / `ScriptCompiler.Compile`): registers the
/// commands; `false` = errors.
pub type CommandLoader = Arc<dyn Fn(&CommandManager) -> bool + Send + Sync>;

/// `CommandManager`.
#[derive(Default)]
pub struct CommandManager {
    /// `_commands` (lower-case name -> command) in dictionary order.
    commands: RwLock<Vec<(String, Arc<dyn Command>)>>,
    /// `_commandAliases` (alias -> command name).
    aliases: RwLock<HashMap<String, String>>,
    /// `AccessLevelManager`.
    pub access_levels: AccessLevelManager,
    loader: RwLock<Option<CommandLoader>>,
}

impl std::fmt::Debug for CommandManager {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("CommandManager")
            .field("commands", &self.get_command_keys())
            .finish()
    }
}

/// `SplitCommandString`: words or double-quoted substrings (`(?<=")[^"]*(?=")|[^" ]+`).
pub fn split_command_string(s: &str) -> Vec<String> {
    let chars: Vec<char> = s.chars().collect();
    let mut out = Vec::new();
    let mut p = 0;
    while p <= chars.len() {
        // Alternative 1: preceded by '"', a run of non-quotes followed by '"'.
        if p > 0 && chars[p - 1] == '"' {
            let mut k = p;
            while k < chars.len() && chars[k] != '"' {
                k += 1;
            }
            if k < chars.len() {
                out.push(chars[p..k].iter().collect());
                p = if k == p { p + 1 } else { k };
                continue;
            }
        }
        // Alternative 2: a run of characters other than '"' and ' '.
        let mut k = p;
        while k < chars.len() && chars[k] != '"' && chars[k] != ' ' {
            k += 1;
        }
        if k > p {
            out.push(chars[p..k].iter().collect());
            p = k;
            continue;
        }
        p += 1;
    }
    out
}

impl CommandManager {
    /// `GetCommandKeys()`.
    pub fn get_command_keys(&self) -> Vec<String> {
        self.commands
            .read()
            .map(|c| c.iter().map(|(k, _)| k.clone()).collect())
            .unwrap_or_default()
    }

    fn get(&self, name: &str) -> Option<Arc<dyn Command>> {
        self.commands
            .read()
            .expect("commands poisoned")
            .iter()
            .find(|(k, _)| k == name)
            .map(|(_, c)| c.clone())
    }

    fn insert(&self, name: String, command: Arc<dyn Command>) {
        let mut cmds = self.commands.write().expect("commands poisoned");
        match cmds.iter_mut().find(|(k, _)| *k == name) {
            // `Remove` + `Add` reuses the freed dictionary slot.
            Some(slot) => slot.1 = command,
            None => cmds.push((name, command)),
        }
    }

    /// `GetCommandInterfaceByName(commandName)` (aliases resolved).
    pub fn get_command_interface_by_name(&self, name: &str) -> Option<Arc<dyn Command>> {
        let lower = name.to_lowercase();
        self.get(&lower).or_else(|| {
            let original = self.aliases.read().ok()?.get(&lower).cloned()?;
            self.get(&original.to_lowercase())
        })
    }

    /// `GetCommandNameBase(aliasName)` (case-sensitive lookups, "" when unknown).
    pub fn get_command_name_base(&self, alias: &str) -> String {
        if let Some(a) = self.aliases.read().ok().and_then(|m| m.get(alias).cloned()) {
            return a;
        }
        if self.get(alias).is_some() {
            return alias.to_string();
        }
        String::new()
    }

    /// `Register(name, command)`.
    pub fn register(&self, name: &str, command: Arc<dyn Command>) {
        self.insert(name.to_lowercase(), command);
    }

    /// `Register(names, command)`: the first name is the command, the others aliases.
    pub fn register_names(&self, names: &[&str], command: Arc<dyn Command>) {
        let Some(first) = names.first() else {
            return;
        };
        let first = first.to_lowercase();
        self.insert(first.clone(), command);
        let mut aliases = self.aliases.write().expect("aliases poisoned");
        for n in &names[1..] {
            aliases.insert(n.to_lowercase(), first.clone());
        }
    }

    /// `UnAliasCommandName(cmd)`.
    pub fn un_alias_command_name(&self, cmd: &str) -> String {
        self.aliases
            .read()
            .ok()
            .and_then(|m| m.get(&cmd.to_lowercase()).cloned())
            .unwrap_or_else(|| cmd.to_string())
    }

    /// `Clear()` (the aliases stay, as in C#).
    pub fn clear(&self) {
        self.commands.write().expect("commands poisoned").clear();
    }

    /// Installs the script load step used by [`CommandManager::reload`].
    pub fn set_loader(&self, loader: CommandLoader) {
        *self.loader.write().expect("loader poisoned") = Some(loader);
    }

    /// `ScriptReflector.Reflect()` / `ScriptCompiler.Compile()`: runs the installed loader
    /// (`true` without one: nothing to load, no errors).
    pub fn reload(&self) -> bool {
        let loader = self.loader.read().ok().and_then(|l| l.clone());
        match loader {
            Some(l) => l(self),
            None => true,
        }
    }

    /// `ForceScriptsReload(character)`.
    fn force_scripts_reload(&self, character: &dyn ICharacter) {
        self.clear();
        if self.reload() {
            character.send_message("[Force Scripts Reload] Success");
        } else {
            character.send_message("|cFFFF0000[Force Scripts Reload] There were errors !|r");
        }
    }

    /// `Handle(character, text, out messageOutput)` for a world player
    /// (`CharacterManager.GetEffectiveAccessLevel` included).
    pub async fn handle(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        text: &str,
    ) -> (bool, CharacterMessageOutput) {
        let level = super::character::effective_access_level(server, player).await;
        let ctx = CommandContext {
            server: Some(server),
            character: PlayerCharacter::arc(player),
        };
        self.handle_with_level(&ctx, level, text)
    }

    /// `Handle` with the character's effective access level already known.
    pub fn handle_with_level(
        &self,
        ctx: &CommandContext<'_>,
        character_access_level: i32,
        text: &str,
    ) -> (bool, CharacterMessageOutput) {
        let mut out = CharacterMessageOutput::new(ctx.character.clone());
        // Un-escape the string, as the client sends it escaped.
        let text = text.replace("@@", "@").replace("||", "|");
        let words = split_command_string(&text);
        let mut this_command = words.first().map(|w| w.to_lowercase()).unwrap_or_default();
        let no_commands = self.commands.read().map(|c| c.is_empty()).unwrap_or(true);

        if no_commands
            && words.len() == 3
            && this_command == "scripts"
            && words[1] == "reload"
            && words[2] == "force"
            && character_access_level >= 100
        {
            self.force_scripts_reload(ctx.character.as_ref());
            return (true, out);
        }

        if no_commands {
            if character_access_level >= 100 {
                out.send_message(&format!(
                    "|cFFFF0000[Error] No commands have been loaded, this is usually because of compile errors. Try using \"{COMMAND_PREFIX}scripts reload\" after the issues have been fixed.|r"
                ));
            } else {
                out.send_message("[Error] No commands available.");
            }
            return (false, out);
        }

        let mut command = self.get(&this_command);
        if command.is_none() {
            let alias = self
                .aliases
                .read()
                .ok()
                .and_then(|m| m.get(&this_command).cloned());
            if let Some(alias) = alias.filter(|a| !a.is_empty()) {
                command = self.get(&alias);
                this_command = alias;
            }
        }
        let Some(command) = command else {
            return (false, out);
        };

        if self.access_levels.get_level(&this_command) > character_access_level {
            out.send_message("|cFFFF0000Insufficient privileges.|r");
            return (true, out);
        }

        let args: Vec<String> = words.iter().skip(1).cloned().collect();
        let result = match command.as_sub_command() {
            Some(sub) => {
                subcommand::pre_execute(sub, ctx, &this_command, &args, &mut out);
                Ok(())
            }
            None => command.execute(ctx, &args, &mut out),
        };
        if let Err(e) = result {
            ctx.character
                .send_message_typed(chat_type::SYSTEM, &e.to_string(), Some(Color::RED));
            tracing::error!("{e}");
            tracing::error!("{e:?}");
        }
        (true, out)
    }

    /// `SendDefaultHelpText(command, messageOutput)`.
    pub fn send_default_help_text(command: &dyn Command, out: &mut dyn MessageOutput) {
        let names = command.command_names();
        let text = match names.first() {
            Some(n) => format!(
                "Help for |cFFFFFFFF{COMMAND_PREFIX}{n}|r |cFFEEEEAA{}|r\n|cFF888888{}|r",
                command.get_command_line_help(),
                command.get_command_help_text()
            ),
            None => "Invalid Command".into(),
        };
        out.send_message_typed(chat_type::SYSTEM, &text, None);
    }

    /// `SendErrorText(command, messageOutput, errorDetails)`.
    pub fn send_error_text(command: &dyn Command, out: &mut dyn MessageOutput, details: &str) {
        let names = command.command_names();
        let text = match names.first() {
            Some(n) => format!("|cFFFFFFFF[{n}]|r |cFFFF0000{details}|r"),
            None => format!("|cFFFF0000Invalid Command - {details}|r"),
        };
        out.send_message_typed(chat_type::SYSTEM, &text, None);
    }

    /// `SendNormalText(command, messageOutput, text)`.
    pub fn send_normal_text(command: &dyn Command, out: &mut dyn MessageOutput, text: &str) {
        let names = command.command_names();
        let text = match names.first() {
            Some(n) => format!("[{n}] {text}"),
            None => format!("[Invalid Command] {text}"),
        };
        out.send_message_typed(chat_type::SYSTEM, &text, None);
    }
}

#[cfg(test)]
mod tests;
