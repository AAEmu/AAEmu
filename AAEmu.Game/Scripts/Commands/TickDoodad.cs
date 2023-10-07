using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;

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
                    doodad.FuncTask.CancelAsync().GetAwaiter().GetResult();
                    doodad.FuncTask.Execute();
                    tickedCount++;
                }
            }
        }
        character.SendMessage("[tickdoodad] phased {0} Doodad(s) with TemplateID {1} - @DOODAD_NAME({1})", tickedCount, unitId);
    }
}
