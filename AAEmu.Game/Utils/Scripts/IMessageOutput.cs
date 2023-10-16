using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts;

public interface IMessageOutput
{
    IEnumerable<string> Messages { get; }
    IEnumerable<string> ErrorMessages { get; }

    void SendMessage(string message);
    void SendMessage(string message, params object[] parameters);
    void SendMessage(Color color, string message, params object[] parameters);
    void SendMessage(ICharacter target, string message, params object[] parameters);
}
