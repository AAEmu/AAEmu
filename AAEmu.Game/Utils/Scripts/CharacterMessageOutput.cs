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

    public void SendMessage(string message)
    {
        _messages.Add(message);
        _character.SendMessage(message);
    }

    public void SendMessage(string message, params object[] parameters)
    {
        _messages.Add(string.Format(message, parameters));
        _character.SendMessage(ChatType.System, message, parameters);
    }
    public void SendMessage(ICharacter target, string message, params object[] parameters)
    {
        _messages.Add($"Target: {target.Name} - {string.Format(message, parameters)}");
        target.SendMessage(message, parameters);
    }

    public void SendErrorMessage(ErrorMessageType messageType, uint type, bool isNotify)
    {
        _errorMessages.Add(messageType.ToString());
        _character.SendErrorMessage(messageType, type, isNotify);
    }

    public void SendMessage(Color color, string message, params object[] parameters)
    {
        _messages.Add($"{color} {string.Format(message, parameters)}");
        _character.SendMessage(color, message, parameters);
    }
}
