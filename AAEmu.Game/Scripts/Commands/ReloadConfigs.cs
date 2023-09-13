using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands
{
    class ReloadConfigs : ICommand
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            string[] name = { "reloadconfig", "reload_configs", "reload_configurations" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Reloads the ConfigurationManager";
        }
        public void Execute(Character character, string[] args)
        {
            try
            {
                if (Program.LoadConfiguration())
                {
                    //ConfigurationManager.Instance.Load();
                    character.SendMessage("Configuration reloaded");
                }
                else
                {
                    character.SendMessage("Configurations failed reloading - check error output");
                }
            }
            catch (Exception e)
            {
                _log.Error(e.Message);
                character.SendMessage("Configurations failed reloading - check error output");
            }
        }
    }
}
