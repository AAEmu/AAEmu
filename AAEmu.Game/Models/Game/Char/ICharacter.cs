using System.Drawing;

namespace AAEmu.Game.Models.Game.Char
{
    public interface ICharacter
    {
        public void SendMessage(string message, params object[] parameters);
        public void SendMessage(Color color, string message, params object[] parameters);
    }
}
