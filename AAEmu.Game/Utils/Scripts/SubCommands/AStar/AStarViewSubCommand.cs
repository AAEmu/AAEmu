using System.Collections.Generic;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Utils.Scripts.SubCommands.AStar;

public class AStarViewSubCommand : SubCommandBase
{
    public AStarViewSubCommand()
    {
        Title = "[AStar View]";
        Description = "Let's map the found waypoints on the terrain.";
        CallPrefix = $"{CommandManager.CommandPrefix}view";
        AddParameter(new NumericSubCommandParameter<uint>("templateId", "template id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var currentTarget = ((Character)character).CurrentTarget;
        var npc = currentTarget as Npc;
        if (currentTarget is null || npc == null)
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: You need to target a Npc first");
            return;
        }

        uint unitTemplateId = parameters["templateId"]; // 1775 Small stone, 5014 Combat Flag, 7221 White Freedom Flag
        if (unitTemplateId == 0)
        {
            unitTemplateId = 5014u;
        }
        if (!DoodadManager.Instance.Exist(unitTemplateId))
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: Doodad templateId:{unitTemplateId} don't exist|r");
            return;
        }

        using var charPos = ((Character)character).Transform.CloneDetached();
        var doodadSpawner = new DoodadSpawner
        {
            Id = 0,
            UnitId = unitTemplateId
        };

        foreach (var point in npc.Ai.PathNode.findPath)
        {
            doodadSpawner.Position = charPos.CloneAsSpawnPosition();

            doodadSpawner.Position.X = point.X;
            doodadSpawner.Position.Y = point.Y;
            doodadSpawner.Position.Z = point.Z;
            ;
            doodadSpawner.Position.Yaw = 0;
            doodadSpawner.Position.Pitch = 0;
            doodadSpawner.Position.Roll = 0;

            var createdDoodad = doodadSpawner.Spawn(0, 0, ((Character)character).ObjId);

            //character.SendMessage($"AStar: Doodad ObjId:{createdDoodad.ObjId}, Template {unitTemplateId} spawned");
        }
    }
}
