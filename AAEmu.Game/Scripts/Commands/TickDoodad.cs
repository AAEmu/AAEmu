using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TickDoodad : ICommand
{
    public string[] CommandNames { get; set; } = ["tickdoodad", "tick_doodad"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<objId>";
    }

    public string GetCommandHelpText()
    {
        return "Moves a doodad onto it's next Phase using <objId> inside a <radius> range.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var radius = 30f;
        if (!uint.TryParse(args[0], out var unitId))
        {
            CommandManager.SendErrorText(this, messageOutput, $"Parse error unitId|r");
            return;
        }

        var tickedCount = 0;
        // Use radius
        var myDoodads = WorldManager.GetAround<Doodad>(character, radius);
        foreach (var doodad in myDoodads)
        {
            if (doodad.TemplateId == unitId)
            {
                if (doodad.FuncTask != null)
                {
                    doodad.FuncTask.Cancel();
                    System.Threading.Tasks.Task.Run(doodad.FuncTask.ExecuteAsync);
                    tickedCount++;
                }
            }
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"Phased {tickedCount} Doodad(s) with TemplateID {unitId} - @DOODAD_NAME({unitId})");
    }
}
