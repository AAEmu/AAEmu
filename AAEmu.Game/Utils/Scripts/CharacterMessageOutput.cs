using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Utils.Scripts;

public class CharacterMessageOutput : IMessageOutput
{
    private List<string> _messages = new();
    private List<string> _errorMessages = new();

    private readonly ICharacter _character;

    public IEnumerable<string> Messages => _messages;
    public IEnumerable<string> ErrorMessages => _errorMessages;

    public CharacterMessageOutput(ICharacter character)
    {
        _character = character;
    }

    public void SendMessage(string message) => SendMessage(ChatType.System, message, null);

    public void SendMessage(ChatType chatType, string message, Color? color = null)
    {
        if (color != null)
            message = $"|c{color.Value.A:X2}{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}{message}|r";
        _messages.Add(message);
        _character.SendMessage(chatType, message);
    }

    public void SendMessage(ICharacter target, string message)
    {
        _messages.Add($"Target: {target.Name} - {message}");
        target.SendMessage(message);
    }

    public void SendErrorMessage(ErrorMessageType messageType, uint type, bool isNotify)
    {
        _errorMessages.Add(messageType.ToString());
        _character.SendErrorMessage(messageType, type, isNotify);
    }
}
