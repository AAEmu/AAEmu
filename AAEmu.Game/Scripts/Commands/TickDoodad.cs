using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TickDoodad : ICommand
{
    // Unused protected static Logger Logger = LogManager.GetCurrentClassLogger();
    public void OnLoad()
    {
        CommandManager.Instance.Register("tickdoodad", this);
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
            character.SendMessage("[tickdoodad] " + CommandManager.CommandPrefix + "tickdoodad " + GetCommandLineHelp());
            return;
        }

        float radius = 30f;
        if (!uint.TryParse(args[0], out var unitId))
        {
            character.SendMessage("|cFFFF0000[tickdoodad] Parse error unitId|r");
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
        character.SendMessage($"[tickdoodad] phased {tickedCount} Doodad(s) with TemplateID {unitId} - @DOODAD_NAME({unitId})");
    }
}
