using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils;
using System.Globalization;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class DeSpawnAll : ICommand
{
    // Unused protected static Logger Logger = LogManager.GetCurrentClassLogger();
    public void OnLoad()
    {
        CommandManager.Instance.Register("despawnall", this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "De-spawns all world objects";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var removedCount = SpawnManager.Instance.DeSpawnAll((byte)character.Transform.WorldId);
        character.SendMessage($"[DeSpawnAll] Removed {removedCount} objects");
    }
}
