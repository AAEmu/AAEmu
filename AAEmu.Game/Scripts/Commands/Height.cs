using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Height : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("height", this);
        }

        public void Execute(Character character, string[] args)
        {
            var height = WorldManager.Instance.GetHeight(character.Position.ZoneId, character.Position.X, character.Position.Y);
            character.SendMessage("C->S: {0} -> {1}", character.Position.Z, height);
        }
    }
}