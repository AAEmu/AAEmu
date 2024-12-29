using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Gimmick;

public class GimmickSpawnSubCommand : SubCommandBase
{
    public GimmickSpawnSubCommand()
    {
        Title = "[Gimmick Spawn]";
        Description = "Spawn one gimmick in front of the player facing player (default) or a optional direction in degrees";
        CallPrefix = $"{CommandManager.CommandPrefix}gimmick spawn";
        AddParameter(new NumericSubCommandParameter<uint>("GimmickTemplateId", "Gimmick template Id", true));
        // AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<facing degrees>", false, "yaw", 0, 360));
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint gimmickTemplateId = parameters["GimmickTemplateId"];

        if (!GimmickManager.Instance.Exist(gimmickTemplateId))
        {
            SendColorMessage(messageOutput, Color.Red, $"Gimmick template {gimmickTemplateId} doesn't exist");
            return;
        }

        var usingCharacter = character as Character;
        var creatorUnit = usingCharacter?.CurrentTarget ?? usingCharacter;

        var spawnEffect = SkillManager.Instance.GetSpawnGimmickEffect(gimmickTemplateId);
        var gimmickSpawner = new GimmickSpawner(spawnEffect, creatorUnit);
        
        usingCharacter?.SendMessage($"Spawned Gimmick TemplateId: {gimmickSpawner.GimmickId} originating from {creatorUnit?.DebugName()}.");
    }
}
