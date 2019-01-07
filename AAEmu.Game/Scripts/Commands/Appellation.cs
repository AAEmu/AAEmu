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

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Appellation] /appellation <Id>");
                return;
            }

            if (uint.TryParse(args[0], out var id))
            {
                if (CharacterManager.Instance.GetAppellationsTemplate(id) == null)
                    character.SendMessage("[Appellation] Id {0} doesn't exist in db...", id);
                else
                    character.Appellations.Add(id);
            }
            else
                character.SendMessage("[Appellation] Throw parse appellationId!");
        }
    }
}