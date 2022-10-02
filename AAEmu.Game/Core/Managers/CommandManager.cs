using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class CommandManager : Singleton<CommandManager>
    {
        public const string CommandPrefix = "/" ;
        private Logger _log = LogManager.GetCurrentClassLogger();
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
            _commands.TryGetValue(commandName.ToLower(), out var command);
            return command;
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


        private void ForceScriptsReload(Character character)
        {
            CommandManager.Instance.Clear();
            if (ScriptCompiler.Compile())
                character.SendMessage("[Force Scripts Reload] Success");
            else
                character.SendMessage("|cFFFF0000[Force Scripts Reload] There were errors !|r");
        }

        private string[] SplitCommandString(string baseString)
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

        public bool Handle(Character character, string text)
        {
            // Un-escape the string, as the client sends it escaped
            // It is required if you want to test things like @NPC_NAME() and |cFF00FFFF text colors |r
            // We only do this for GM commands as it would cause problems with regular chat
            text = text.Replace("@@", "@").Replace("||", "|");

            var words = SplitCommandString(text);
            // var words = text.Split(' ');
            var thisCommand = words.Length > 0 ? words[0].ToLower() : "";

            // Only enable the force_scripts_reload when we don't have anything loaded, this is simply a failsafe function in case
            // things aren't working out when live-editing scripts
            if ((_commands.Count <= 0) && (words.Length == 3) && (thisCommand == "scripts") && (words[1] == "reload") && (words[2] == "force") && (character.AccessLevel >= 100))
            {
                ForceScriptsReload(character);

                return true;
            }

            if (_commands.Count <= 0)
            {
                // Only display extended error to admins
                if (character.AccessLevel >= 100)
                    character.SendMessage(
                        "|cFFFF0000[Error] No commands have been loaded, this is usually because of compile errors. Try using \"" +
                        CommandManager.CommandPrefix + "scripts reload\" after the issues have been fixed.|r");
                else
                    character.SendMessage(
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

            if (AccessLevelManager.Instance.GetLevel(thisCommand) > character.AccessLevel)
            {
                character.SendMessage("|cFFFF0000Insufficient privileges.|r");
                return true;
            }

            var args = new string[words.Length - 1];
            if (words.Length > 1)
                Array.Copy(words, 1, args, 0, words.Length - 1);

            try
            {
                if (command is ICommandV2 subcommand)
                {
                    subcommand.PreExecute(character, thisCommand, args);
                }
                else
                {
                    command.Execute(character, args);
                }
            }
            catch (Exception e)
            {
                character.SendMessage(Color.Red, e.Message);
                _log.Error(e.Message);
                _log.Error(e.StackTrace);
            }
            
            return true;
        }

        public void Clear()
        {
            _commands.Clear();
        }
    }
}
