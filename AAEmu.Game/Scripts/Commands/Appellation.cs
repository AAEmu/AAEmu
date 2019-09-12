using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Appellation : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("appellation", this);
        }

        public string GetCommandLineHelp()
        {
            return "<titleId>";
        }

        public string GetCommandHelpText()
        {
            return "Sets your current title using <titleId>";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Appellation] /appellation <titleId>");
                return;
            }

            if (uint.TryParse(args[0], out var id))
            {
                if (CharacterManager.Instance.GetAppellationsTemplate(id) == null)
                    character.SendMessage("[Appellation] <titleId> {0} doesn't exist in the database ...", id);
                else
                    character.Appellations.Add(id);
            }
            else
                character.SendMessage("|cFFFF0000[Appellation] Error parsing <titleId> !|r");
        }
    }
}
