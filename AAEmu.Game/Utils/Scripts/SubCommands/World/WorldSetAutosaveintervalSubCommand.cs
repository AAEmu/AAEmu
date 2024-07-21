using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetAutosaveintervalSubCommand : SubCommandBase
{
    public WorldSetAutosaveintervalSubCommand()
    {
        Title = "[World Set AutoSaveInterval]";
        Description = "Setting the Auto Save Interval";
        CallPrefix = $"{CommandManager.CommandPrefix}autosaveinterval";
        AddParameter(new NumericSubCommandParameter<float>("AutoSaveInterval", "AutoSaveInterval", true));
    }
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        float autoSaveInterval = parameters["AutoSaveInterval"];
        if (autoSaveInterval < 1.0f || autoSaveInterval > 1440.0f)
        {
            SendColorMessage(messageOutput, Color.Coral, $"AutoSaveInterval = {autoSaveInterval} must be at least 1.0 and no more than 1440.0 minutes");
            return;
        }
        character.SetAutoSaveInterval(autoSaveInterval);
        SaveManager.Instance.SetAutoSaveInterval();

        SendMessage(messageOutput, $"Set AutoSaveInterval {autoSaveInterval}");
        Logger.Warn($"{Title}: {autoSaveInterval}");
    }
}
