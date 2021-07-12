using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands
{
    public class Scripts : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("scripts", this);
        }

        public string GetCommandLineHelp()
        {
            return "<action>";
        }

        public string GetCommandHelpText()
        {
            return "Does script related actions. Allowed <action> are: reload, reboot, save";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Scripts] Using: " + CommandManager.CommandPrefix + "scripts <action>");
                //character.SendMessage("[Scripts] Action: reload");
                return;
            }

            switch (args[0])
            {
                case "reload":
                case "reboot":
                    CommandManager.Instance.Clear();
                    if (ScriptCompiler.Compile())
                        character.SendMessage("[Scripts] Reload - Success");
                    else
                        character.SendMessage("|cFFFF0000[Scripts] Reload - There were errors, please check the server logs for details !|r");
                    break;
                case "save":
                    if (SaveManager.Instance.DoSave())
                        character.SendMessage("[Scripts] Save - Done saving user database");
                    else
                        character.SendMessage("|cFFFF0000[Scripts] Save - Failed saving user database, was possible already in the process of saving, please check server console for details.|r");
                    break;
                case "reloadslavepoints":
                    SlaveManager.Instance.LoadSlaveAttachmentPointLocations();
                    character.SendMessage("[Scripts] Slave Attachment Point Locations .json Reloaded");
                    break;
                default:
                    character.SendMessage("|cFFFF0000[Scripts] Undefined action...|r");
                    break;
            }
        }
    }
}
