using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Dist : ICommand
{
    public void OnLoad()
    {
        string[] name = { "dist", "distance" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
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
            character.SendMessage($"[Distance] No target selected");
            return;
        }

        var rawDistance = MathUtil.CalculateDistance(character.Transform.World.Position, target.Transform.World.Position);
        var rawDistanceZ = MathUtil.CalculateDistance(character.Transform.World.Position, target.Transform.World.Position, true);
        var modelDistance = character.GetDistanceTo(target);
        var modelDistanceZ = character.GetDistanceTo(target, true);
        var angleWorld = MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(character.Transform.World.Position, target.Transform.World.Position) - 90.0);
        var angle = MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(character, target));

        character.SendMessage($"[Distance]\nRaw distance : {rawDistance}\n" +
                              $"Raw distance (Z) : {rawDistanceZ}\n" +
                              $"Model adjusted distance: {modelDistance}\n" +
                              $"Model adjusted distance (Z): {modelDistanceZ}\n" +
                              $"World Angle: {angleWorld:F1}°\n" +
                              $"Relative Angle: {angle:F1}°");
    }
}
