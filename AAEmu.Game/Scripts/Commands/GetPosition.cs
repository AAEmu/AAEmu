using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class GetPosition : ICommand
{
    public string[] CommandNames { get; set; } = ["position", "pos"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(player)";
    }

    public string GetCommandHelpText()
    {
        return
            "Displays information about the position of you, or your target if a target is selected or provided as a argument.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget != null && character.CurrentTarget != character)
        {
            var pos = character.CurrentTarget.Transform.CloneAsSpawnPosition();

            if (character.CurrentTarget is Npc npc)
            {
                CommandManager.SendNormalText(this, messageOutput,
                    $"Id: {npc.Spawner.Id}, ObjId: {character.CurrentTarget.ObjId}, TemplateId: {npc.TemplateId} X: |cFFFFFFFF{pos.X}|r  Y: |cFFFFFFFF{pos.Y}|r  Z: |cFFFFFFFF{pos.Z}|r");
            }
        }
        else
        {
            var targetPlayer = character;
            if (args.Length > 0)
            {
                targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);
            }

            var pos = targetPlayer.Transform.CloneAsSpawnPosition();

            var zonename = "???";
            var zone = ZoneManager.Instance.GetZoneByKey(pos.ZoneId);
            if (zone != null)
            {
                zonename = "@ZONE_NAME(" + zone.Id.ToString() + ")";
            }

            CommandManager.SendNormalText(this, messageOutput,
                $"|cFFFFFFFF{targetPlayer.Name}|r X: |cFFFFFFFF{pos.X:F1}|r  Y: |cFFFFFFFF{pos.Y:F1}|r  Z: |cFFFFFFFF{pos.Z:F1}|r  RotZ: |cFFFFFFFF{pos.Yaw:F0}|r  ZoneId: |cFFFFFFFF{pos.ZoneId}|r {zonename}  SubZoneId: |cFFFFFFFF{character.SubZoneId}|r");
        }
    }
}
