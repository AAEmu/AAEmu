using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Slaves;

public class SlaveSpawnSubCommand : SubCommandBase
{
    public SlaveSpawnSubCommand()
    {
        Title = "[Slave Spawn]";
        Description = "Spawn one slave in front of the player facing player (default) or a optional direction in degrees";
        CallPrefix = $"{CommandManager.CommandPrefix}slave spawn";
        AddParameter(new NumericSubCommandParameter<uint>("TemplateId", "Slave template Id", true));
        AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<facing degrees>", false, "yaw", 0, 360));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint templateId = parameters["TemplateId"];

        if (!SlaveManager.Instance.Exist(templateId))
        {
            SendColorMessage(messageOutput, Color.Red, $"Slave template {templateId} doesn't exist|r");
            return;
        }

        var owner = (Character)character;
        SlaveManager.Instance.Create(owner, null, templateId);
    }
}
