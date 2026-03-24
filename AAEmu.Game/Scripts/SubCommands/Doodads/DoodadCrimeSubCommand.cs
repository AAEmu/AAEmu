using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadCrimeSubCommand : SubCommandBase
{
    public DoodadCrimeSubCommand()
    {
        Title = "[Doodad Spawn Crime]";
        Description = "Add a new doodad of a specific template 3 meters in front of the player. Default yaw will use characters facing angle.";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad crime";
        AddParameter(new NumericSubCommandParameter<uint>("templateId", "template id", true));
        AddParameter(new NumericSubCommandParameter<uint>("owner", "owner=<playerId>", true, "owner"));
        AddParameter(new NumericSubCommandParameter<uint>("victim", "victim=<playerId>", true, "victim"));
        AddParameter(new NumericSubCommandParameter<uint>("source", "source=<doodadTemplateId>", false, "source"));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint unitTemplateId = parameters["templateId"];
        uint ownerId = parameters["owner"];
        uint victimId = parameters["victim"];
        uint sourceDoodadTemplateId = parameters["source"];
        if (!DoodadManager.Instance.Exist(unitTemplateId))
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad templateId:{unitTemplateId} don't exist");
            return;
        }

        using var charPos = character.Transform.CloneDetached();
        charPos.Local.AddDistanceToFront(3f);
        var defaultYaw = (float)MathUtil.CalculateAngleFrom(charPos, character.Transform);
        var doodadSpawner = new DoodadSpawner
        {
            Id = 0,
            UnitId = unitTemplateId,
            ParentWorld = ((Character)character).ParentWorld,
            Position = charPos.CloneAsSpawnPosition(),
        };

        doodadSpawner.Position.Yaw = defaultYaw;
        doodadSpawner.Position.Pitch = 0;
        doodadSpawner.Position.Roll = 0;
        
        var doodad = DoodadManager.Instance.Create(((Character)character).ParentWorld, 0, unitTemplateId);

        if (doodad == null)
        {
            Logger.Warn($"Doodad {unitTemplateId}, from spawn not exist at db");
            return;
        }

        doodad.OwnerId = ownerId;
        doodad.OwnerType = DoodadOwnerType.Character;
        doodad.Spawner = doodadSpawner;
        doodad.Transform.ApplyWorldSpawnPosition(doodadSpawner.Position);
        doodad.QuestGlow = 0u; // TODO: make this OOP
        doodad.ItemTemplateId = sourceDoodadTemplateId;
        doodad.Data = (int)victimId;
        doodad.PlantTime = DateTime.UtcNow;
        doodad.Spawn();

        character.SendMessage($"Crime Doodad ObjId:{doodad.ObjId}, Template {unitTemplateId} spawned, Owner: {ownerId}, Victim: {victimId}, Source: {sourceDoodadTemplateId}");
    }
}
