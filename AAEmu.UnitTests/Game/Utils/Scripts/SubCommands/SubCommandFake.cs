using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

public class SubCommandFake : SubCommandBase
{
    public IDictionary<string, ParameterValue> Parameters { get; private set; }
    public bool Executed { get; private set; }
    public SubCommandFake(IEnumerable<SubCommandParameterBase> parameterDefinitions)
    {
        Title = "[Test]";
        Description = "Test Subcommand";
        CallPrefix = $"{CommandManager.CommandPrefix}test";
        foreach (var parameterDefinition in parameterDefinitions)
        {
            AddParameter(parameterDefinition);
        }
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Executed = true;
        Parameters = parameters;
    }

    public void BaseSendHelpMessage(IMessageOutput messageOutput)
    {
        base.SendHelpMessage(messageOutput);
    }
}
