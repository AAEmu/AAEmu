using System.Drawing;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Char
{
    public interface ICharacter : IUnit
    {
        public void SendMessage(string message, params object[] parameters);
        public void SendMessage(Color color, string message, params object[] parameters);
    }
}
