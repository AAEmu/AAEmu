using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers
{
    public class CommandManager : Singleton<CommandManager>
    {
        private Dictionary<string, ICommand> _commands;

        private CommandManager()
        {
            _commands = new Dictionary<string, ICommand>();
        }

        public List<string> GetCommandKeys()
        {
            return _commands.Keys.ToList();
        }

        public void Register(string name, ICommand command)
        {
            if (_commands.ContainsKey(name.ToLower()))
                _commands.Remove(name.ToLower());
            _commands.Add(name.ToLower(), command);
        }

        public bool Handle(Character character, string text)
        {
            var words = text.Split(' ');
            _commands.TryGetValue(words[0].ToLower(), out var command);
            if (command == null)
                return false;

            if(AccessLevel.getLevel(words[0].ToLower()) > character.AccessLevel){
                character.SendMessage("Insufficient privileges.");
                return false;
            }

            var args = new string[words.Length - 1];
            if (words.Length > 1)
                Array.Copy(words, 1, args, 0, words.Length - 1);
            command.Execute(character, args);
            return true;
        }

        public void Clear()
        {
            _commands.Clear();
        }
    }
}