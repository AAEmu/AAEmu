using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Utils.Scripts;

public interface IMessageOutput
{
    IEnumerable<string> Messages { get; }
    IEnumerable<string> ErrorMessages { get; }

    void SendMessage(string message);
    void SendMessage(ChatType chatType, string message, Color? color = null);
    void SendMessage(ICharacter target, string message);
}
