using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeFakeTrialSubCommand : SubCommandBase
{
    public CrimeFakeTrialSubCommand()
    {
        Title = "[Crime Fake Trial]";
        Description = "Starts a fake trial for target player";
        CallPrefix = $"{CommandManager.CommandPrefix}crime fake";
        AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
        AddParameter(new StringSubCommandParameter("victim", "player name||target||self", true));
        AddParameter(new NumericSubCommandParameter<uint>("evidence", "evidence=<count>", true, "evidence"));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string,
        ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Character targetCharacter;
        var selfCharacter = (Character)character;

        var firstParameter = parameters["target"].ToString();
        if (firstParameter == "target")
        {
            if (selfCharacter.CurrentTarget is null || selfCharacter.CurrentTarget is not Character)
            {
                SendColorMessage(messageOutput, Color.Red, "Please select a valid character player");
                return;
            }

            targetCharacter = selfCharacter.CurrentTarget as Character;
        }
        else if (firstParameter == "self")
        {
            targetCharacter = selfCharacter;
        }
        else
        {
            var player = WorldManager.Instance.GetCharacter(firstParameter);
            if (player is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Character player: {firstParameter} was not found.");
                return;
            }

            targetCharacter = player;
        }

        if (targetCharacter is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Character player: {firstParameter} was not found.");
            return;
        }

        var victimCharacter = (Character)character;

        var secondParameter = parameters["victim"].ToString();
        if (secondParameter == "target")
        {
            if (selfCharacter.CurrentTarget is null || selfCharacter.CurrentTarget is not Character)
            {
                SendColorMessage(messageOutput, Color.Red, "Please select a valid character victim");
                return;
            }

            victimCharacter = selfCharacter.CurrentTarget as Character;
        }
        else if (secondParameter == "self")
        {
            victimCharacter = selfCharacter;
        }
        else
        {
            var player = WorldManager.Instance.GetCharacter(secondParameter);
            if (player is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Character victim: {secondParameter} was not found.");
                return;
            }

            victimCharacter = player;
        }

        if (victimCharacter is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Character victim: {secondParameter} was not found.");
            return;
        }

        if (targetCharacter == victimCharacter)
        {
            SendColorMessage(messageOutput, Color.Red, $"The victim can not be the same as the perpetrator.");
            return;
        }

        uint evidenceCount = parameters["evidence"];
        SendColorMessage(messageOutput, Color.White, $"Putting {targetCharacter.Name} on trial by using {evidenceCount} new evidence(s) to frame them with {victimCharacter.Name} as the victim.");

        // Generate evidence
        for (var i = 0; i < evidenceCount; i++)
        {
            var pos = targetCharacter.Transform.World.Position;
            Doodad evidence = null;
            Doodad fakePine = null;
            uint skillId = 0;
            var rng = Random.Shared.Next(0, 3);
            // rng = 1; // for fixed murder
            switch (rng)
            {
                case 0: evidence = CrimeManager.Instance.GenerateEvidenceFromDamage(targetCharacter, victimCharacter);
                    break;
                case 1: evidence = CrimeManager.Instance.GenerateEvidenceFromKill(targetCharacter, victimCharacter);
                    break;
                case 2:
                    fakePine = DoodadManager.Instance.CreatePlayerDoodad(victimCharacter, 398, pos.X, pos.Y, pos.Z, 0, 1f, 0, FarmType.Invalid, 14898, 0, true);
                    fakePine.Spawn();
                    skillId = 13789; // uproot
                    evidence = CrimeManager.Instance.GenerateEvidenceFromTheft(targetCharacter, fakePine);
                    break;
            }

            if (evidence is not null)
            {
                var evidenceFunc = evidence.CurrentFuncs?.FirstOrDefault();
                _ = CrimeManager.Instance.ReportCrime(victimCharacter, evidence, skillId, evidenceFunc?.NextPhase ?? 0, evidenceFunc?.FuncId ?? 0, $"Report #{i+1}");
                if (fakePine is not null)
                    fakePine.Delete();
            }
        }

        TrialManager.Instance.ArrestCriminal(targetCharacter, victimCharacter);
    }
}
