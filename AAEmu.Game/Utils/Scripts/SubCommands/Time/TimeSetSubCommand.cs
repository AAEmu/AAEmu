using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.Time;

public class TimeSetSubCommand : SubCommandBase
{
    public TimeSetSubCommand()
    {
        Title = "[Time Information]";
        Description = "Find out what time it is";
        CallPrefix = $"{CommandManager.CommandPrefix}time set";
        AddParameter(new NumericSubCommandParameter<int>("hour", "hour", true));
        AddParameter(new NumericSubCommandParameter<int>("minute", "minute", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var oldTime = TimeManager.Instance.GetTime;
        var hour = (int)oldTime ;
        if (parameters.ContainsKey("hour"))
            hour = (int)parameters["hour"];
        var minute = (int)Math.Floor((oldTime - Math.Truncate(oldTime)) * 60f);
        if (parameters.ContainsKey("minute"))
            minute = parameters["minute"];
        var newTime = hour * 1f + (minute / 60f);
        TimeManager.Instance.Set(newTime);

        SendMessage(messageOutput, $"Changed game time {oldTime:F2} -> {newTime:F2} ({hour}h{minute}m)");
    }
}
