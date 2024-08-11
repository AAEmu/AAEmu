using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class BuildHouse : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "build", "build_house" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[stepcount]";
    }

    public string GetCommandHelpText()
    {
        return "Advances the target house further by [stepcount] or 1 if omitted";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget is not House targetHouse)
        {
            CommandManager.SendErrorText(this, messageOutput, "You must target a house");
            return;
        }

        var buildActionCount = 1u;
        if (args.Length > 0)
        {
            if (uint.TryParse(args[0], out var val))
            {
                buildActionCount = val;
            }
        }

        var actionsLeftForStep = targetHouse.AllAction - targetHouse.CurrentAction;
        if (buildActionCount > actionsLeftForStep)
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Cannot do {buildActionCount} build actions when the maximum allowed for the current step is {actionsLeftForStep}");
            return;
        }

        for (var i = 0; i < buildActionCount; i++)
        {
            targetHouse.AddBuildAction();
            character.BroadcastPacket(
                new SCHouseBuildProgressPacket(
                    targetHouse.TlId,
                    targetHouse.ModelId,
                    targetHouse.AllAction,
                    targetHouse.CurrentStep == -1 ? targetHouse.AllAction : targetHouse.CurrentAction
                ),
                true
            );
        }

        if (targetHouse.CurrentStep == -1)
        {
            var doodads = targetHouse.AttachedDoodads.ToArray();
            foreach (var doodad in doodads)
            {
                doodad.Spawn();
            }
        }
    }
}
