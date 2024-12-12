using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Fly : ICommand
{
    public string[] CommandNames { get; set; } = ["fly"];
    private static List<uint> characterFlyStateCache = [];

    private static bool GetCacheState(uint characterId)
    {
        return characterFlyStateCache.Contains(characterId);
    }

    private static void SetCacheState(uint characterId, bool state)
    {
        if (state && !GetCacheState(characterId))
        {
            characterFlyStateCache.Add(characterId);
        }
        else
        {
            characterFlyStateCache.Remove(characterId);
        }
    }

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [true || false]";
    }

    public string GetCommandHelpText()
    {
        return "Enables or disables fly-mode (also makes you move at high-speed)";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = character;

        var firstArg = 0;
        if (args.Length > 0)
        {
            targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out firstArg);
        }

        var isFlying = !GetCacheState(targetPlayer.Id); // We cache the playerId, not the ObjectId

        if (args.Length > firstArg)
        {
            if (!bool.TryParse(args[firstArg + 0], out isFlying))
            {
                CommandManager.SendErrorText(this, messageOutput, $"<fly state> bool parse error!");
                return;
            }
        }

        targetPlayer.SendPacket(new SCUnitFlyingStateChangedPacket(targetPlayer.ObjId, isFlying));
        targetPlayer.SendMessage($"State changed to |cFFFFFFFF{isFlying}|r.");
        SetCacheState(targetPlayer.Id, isFlying);
    }
}
