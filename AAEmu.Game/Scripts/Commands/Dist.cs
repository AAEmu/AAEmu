using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Dist : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "distance", "dist" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[objId]";
    }

    public string GetCommandHelpText()
    {
        return "Gets distance using various calculations with target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var target = character.CurrentTarget;
        if (target == null)
        {
            CommandManager.SendNormalText(this, messageOutput, $"No target selected");
            return;
        }

        if (args.Length > 0)
        {
            if (uint.TryParse(args[0], out var targetObjId))
            {
                var go = WorldManager.Instance.GetGameObject(targetObjId);
                if (go is BaseUnit bu)
                {
                    target = bu;
                }
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, $"[objId] parse error");
                return;
            }
        }

        var rawDistance =
            MathUtil.CalculateDistance(character.Transform.World.Position, target.Transform.World.Position);
        var rawDistanceZ =
            MathUtil.CalculateDistance(character.Transform.World.Position, target.Transform.World.Position, true);
        var modelDistance = character.GetDistanceTo(target);
        var modelDistanceZ = character.GetDistanceTo(target, true);
        var angleWorld =
            MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(character.Transform.World.Position,
                target.Transform.World.Position) - 90.0);
        var angle = MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(character, target));

        CommandManager.SendNormalText(this, messageOutput, $"\n" +
                                                           $"Raw distance : {rawDistance}\n" +
                                                           $"Raw distance (Z) : {rawDistanceZ}\n" +
                                                           $"Model adjusted distance: {modelDistance}\n" +
                                                           $"Model adjusted distance (Z): {modelDistanceZ}\n" +
                                                           $"World Angle: {angleWorld:F1}°\n" +
                                                           $"Relative Angle: {angle:F1}°");
    }
}
