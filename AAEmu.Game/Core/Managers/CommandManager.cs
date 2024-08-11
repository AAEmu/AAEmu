using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CommandManager : Singleton<CommandManager>
{
    public const string CommandPrefix = "/";
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Dictionary<string, ICommand> _commands;
    private Dictionary<string, string> _commandAliases;

    private CommandManager()
    {
        _commands = new Dictionary<string, ICommand>();
        _commandAliases = new Dictionary<string, string>();
    }

    public List<string> GetCommandKeys()
    {
        return _commands.Keys.ToList();
    }

    public ICommand GetCommandInterfaceByName(string commandName)
    {
        var cmd = _commands.GetValueOrDefault(commandName.ToLower());

        // If not found, check if it's an alias
        if ((cmd == null) && (_commandAliases.TryGetValue(commandName.ToLower(), out var originalName)))
            cmd = _commands.GetValueOrDefault(originalName.ToLower());

        return cmd;
    }

    public string GetCommandNameBase(string aliasName)
    {
        if (_commandAliases.TryGetValue(aliasName, out var aName))
            return aName;
        if (_commands.TryGetValue(aliasName, out _))
            return aliasName;
        return "";
    }
    
    public void Register(string name, ICommand command)
    {
        if (_commands.ContainsKey(name.ToLower()))
            _commands.Remove(name.ToLower());
        _commands.Add(name.ToLower(), command);
    }

    public void Register(string[] names, ICommand command)
    {
        if (names.Length <= 0)
            return;
        if (_commands.ContainsKey(names[0].ToLower()))
            _commands.Remove(names[0].ToLower());
        _commands.Add(names[0].ToLower(), command);

        for (int i = 1; i < names.Length; i++)
        {
            if (_commandAliases.ContainsKey(names[i].ToLower()))
                _commandAliases.Remove(names[i].ToLower());
            _commandAliases.Add(names[i].ToLower(), names[0].ToLower());
        }
    }

    private static void ForceScriptsReload(Character character)
    {
        CommandManager.Instance.Clear();
        if (ScriptCompiler.Compile())
            character.SendMessage("[Force Scripts Reload] Success");
        else
            character.SendMessage("|cFFFF0000[Force Scripts Reload] There were errors !|r");
    }

    private static string[] SplitCommandString(string baseString)
    {
        // https://codereview.stackexchange.com/questions/10826/splitting-a-string-into-words-or-double-quoted-substrings
        var re = new Regex("(?<=\")[^\"]*(?=\")|[^\" ]+");
        return re.Matches(baseString).Cast<Match>().Select(m => m.Value).ToArray();
    }

    public string UnAliasCommandName(string cmd)
    {
        if (_commandAliases.TryGetValue(cmd.ToLower(), out var realName))
            return realName;
        else
            return cmd;
    }

    public bool Handle(Character character, string text, out IMessageOutput messageOutput)
    {
        messageOutput = new CharacterMessageOutput(character);

        // Un-escape the string, as the client sends it escaped
        // It is required if you want to test things like @NPC_NAME() and |cFF00FFFF text colors |r
        // We only do this for GM commands as it would cause problems with regular chat
        text = text.Replace("@@", "@").Replace("||", "|");

        var words = SplitCommandString(text);
        // var words = text.Split(' ');
        var thisCommand = words.Length > 0 ? words[0].ToLower() : "";

        var characterAccessLevel = CharacterManager.Instance.GetEffectiveAccessLevel(character);

        // Only enable the force_scripts_reload when we don't have anything loaded, this is simply a failsafe function in case
        // things aren't working out when live-editing scripts
        if ((_commands.Count <= 0) && (words.Length == 3) && (thisCommand == "scripts") && (words[1] == "reload") && (words[2] == "force") && (characterAccessLevel >= 100))
        {
            ForceScriptsReload(character);

            return true;
        }

        if (_commands.Count <= 0)
        {
            // Only display extended error to admins
            if (characterAccessLevel >= 100)
                messageOutput.SendMessage(
                    "|cFFFF0000[Error] No commands have been loaded, this is usually because of compile errors. Try using \"" +
                    CommandManager.CommandPrefix + "scripts reload\" after the issues have been fixed.|r");
            else
                messageOutput.SendMessage(
                    "[Error] No commands available.");
            return false;
        }

        _commands.TryGetValue(thisCommand, out var command);
        if (command == null)
        {
            _commandAliases.TryGetValue(thisCommand, out var alias);
            if ((alias != null) && (alias != string.Empty))
            {
                _commands.TryGetValue(alias, out command);
                thisCommand = alias;
            }
        }

        if (command == null)
            return false;

        if (AccessLevelManager.Instance.GetLevel(thisCommand) > characterAccessLevel)
        {
            messageOutput.SendMessage("|cFFFF0000Insufficient privileges.|r");
            return true;
        }

        var args = new string[words.Length - 1];
        if (words.Length > 1)
            Array.Copy(words, 1, args, 0, words.Length - 1);

        try
        {
            if (command is ICommandV2 subcommand)
            {
                subcommand.PreExecute(character, thisCommand, args, messageOutput);
            }
            else
            {
                command.Execute(character, args, messageOutput);
            }
        }
        catch (Exception e)
        {
            character.SendMessage(ChatType.System, e.Message, Color.Red);
            Logger.Error(e.Message);
            Logger.Error(e.StackTrace);
        }

        return true;
    }

    public void Clear()
    {
        _commands.Clear();
    }

    public static void SendDefaultHelpText(ICommand command, IMessageOutput messageOutput)
    {
        messageOutput.SendMessage(ChatType.System, command.CommandNames.Length > 0
            ? $"Help for |cFFFFFFFF{CommandPrefix}{command.CommandNames[0]}|r |cFFEEEEAA{command.GetCommandLineHelp()}|r\n|cFF888888{command.GetCommandHelpText()}|r"
            : "Invalid Command");
    }

    public static void SendErrorText(ICommand command, IMessageOutput messageOutput, string errorDetails)
    {
        messageOutput.SendMessage(ChatType.System, command.CommandNames.Length > 0
            ? $"|cFFFFFFFF[{command.CommandNames[0]}]|r |cFFFF0000{errorDetails}|r"
            : $"|cFFFF0000Invalid Command - {errorDetails}|r");
    }

    public static void SendNormalText(ICommand command, IMessageOutput messageOutput, string text)
    {
        messageOutput.SendMessage(ChatType.System, command.CommandNames.Length > 0
            ? $"[{command.CommandNames[0]}] {text}"
            : $"[Invalid Command] {text}");
    }
}
