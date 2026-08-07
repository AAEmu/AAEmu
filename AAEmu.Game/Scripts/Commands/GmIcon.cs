using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class GmIcon : ICommand
{
    public string[] CommandNames { get; set; } = ["gm"];

    private static readonly ConcurrentDictionary<uint, bool> ActiveByObjId = new();

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Toggles the GM icon next to your name.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var enabled = !ActiveByObjId.GetOrAdd(character.ObjId, false);
        ActiveByObjId[character.ObjId] = enabled;
        character.SendGmModeChanged(6, (byte)(enabled ? 1 : 0));
        CommandManager.SendNormalText(this, messageOutput, enabled ? "GM icon activated." : "GM icon deactivated.");
    }
}
