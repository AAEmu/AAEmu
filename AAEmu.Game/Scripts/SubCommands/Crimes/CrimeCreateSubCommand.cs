using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeCreateSubCommand : SubCommandBase
{
    public CrimeCreateSubCommand()
    {
        Title = "[Crime Create]";
        Description = "Add a new crime evidence doodad of a specific template at your current location.\n" +
                      $"Valid templates: Small Bloodstain ({DoodadConstants.SmallBloodstain}), Large Bloodstain ({DoodadConstants.LargeBloodstain}), Footprint (male {DoodadConstants.FootprintMale}, female {DoodadConstants.FootprintFemale})";
        CallPrefix = $"{CommandManager.CommandPrefix}crime create";
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

        if (unitTemplateId != DoodadConstants.SmallBloodstain && unitTemplateId != DoodadConstants.LargeBloodstain && unitTemplateId != DoodadConstants.FootprintMale && unitTemplateId != DoodadConstants.FootprintFemale)
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad templateId:{unitTemplateId} is not related to evidence.");
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
            Logger.Warn($"Evidence {unitTemplateId}, was not able to spawn");
            return;
        }

        doodad.OwnerId = ownerId;
        doodad.OwnerType = DoodadOwnerType.Character;
        doodad.Spawner = doodadSpawner;
        doodad.Transform.ApplyWorldSpawnPosition(doodadSpawner.Position);
        doodad.QuestGlow = 0u;
        doodad.ItemTemplateId = sourceDoodadTemplateId;
        doodad.Data = (int)victimId;
        doodad.PlantTime = DateTime.UtcNow;
        doodad.Spawn();

        character.SendMessage($"Crime Evidence Doodad ObjId:{doodad.ObjId}, Template {unitTemplateId} spawned, Owner: {ownerId}, Victim: {victimId}, Source: {sourceDoodadTemplateId}");
    }
}
