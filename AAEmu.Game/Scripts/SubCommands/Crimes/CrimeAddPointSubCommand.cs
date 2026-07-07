using System.Drawing;
using System.Globalization;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeAddPointSubCommand : SubCommandBase
{
    public CrimeAddPointSubCommand()
    {
        Title = "[Crime Points]";
        Description = "Changes the amount of crime, infamy or jury points of target player";
        CallPrefix = $"{CommandManager.CommandPrefix}crime points";
        AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
        AddParameter(new NumericSubCommandParameter<int>("crime", "crime point amount", false, "crime"));
        AddParameter(new NumericSubCommandParameter<int>("infamy", "infamy amount", false, "infamy"));
        AddParameter(new NumericSubCommandParameter<int>("jury", "jury points amount", false, "jury"));
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

        short crimePoints = (short)GetOptionalParameterValue(parameters, "crime", 0);
        var infamyPoints = GetOptionalParameterValue(parameters, "infamy", 0);
        var juryPoints = GetOptionalParameterValue(parameters, "jury", 0);

        if (crimePoints != 0 || infamyPoints != 0 || juryPoints != 0)
        {
            // If something changed, go to change mode
            targetCharacter.CrimePoint += crimePoints;
            targetCharacter.InfamyPoint += infamyPoints;
            targetCharacter.SendPacket(new SCCrimeChangedPacket(crimePoints, targetCharacter.CrimePoint, targetCharacter.InfamyPoint, targetCharacter.GetCrimeState()));
            targetCharacter.JuryPoint += juryPoints;

            var modifiedString = $"crime {(crimePoints < 0 ? "" : "+")}{crimePoints}, infamy {(infamyPoints < 0 ? "" : "+")}{infamyPoints}, jury points {(juryPoints < 0 ? "" : "+")}{juryPoints}";
            SendMessage(messageOutput, $"Adjusted {targetCharacter.Name}'s justice points; {modifiedString}");
            if (selfCharacter.Id != targetCharacter.Id)
            {
                SendMessage(targetCharacter, messageOutput,
                    $"[GM] {selfCharacter.Name} has changed your justice points; {modifiedString}");
            }
        }
        else
        {
            // If nothing changed, show target player's stats instead.
            SendColorMessage(messageOutput, Color.White, $"[{targetCharacter.Name}] justice stats;");
            SendMessage(messageOutput, $"[Faction] {targetCharacter.Faction.Id} (MotherId: {targetCharacter.Faction?.MotherId})");
            SendMessage(messageOutput, $"[Crime] {targetCharacter.CrimePoint}");
            SendMessage(messageOutput, $"[Infamy] {targetCharacter.InfamyPoint}");
            SendMessage(messageOutput, $"[Jury] {targetCharacter.JuryPoint}");
            // TODO: Add trial/guilty counts
        }
    }
}
