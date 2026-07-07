using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Crimes;

public class CrimeCourtSubCommand : SubCommandBase
{
    public CrimeCourtSubCommand()
    {
        Title = "[Crime Court]";
        Description = "Does a jury invite request";
        CallPrefix = $"{CommandManager.CommandPrefix}crime court";
        AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
        AddParameter(new StringSubCommandParameter("action", "none||list", true));
        AddParameter(new NumericSubCommandParameter<uint>("court", "court=<courtRoomId>", false, "court"));
        AddParameter(new NumericSubCommandParameter<uint>("trial", "trial=<trialId>", false, "trial"));
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

        var action = GetOptionalParameterValue(parameters, "action", "none").ToLower();
        SendColorMessage(messageOutput, Color.White, $"[Crime Court {action}] Action for {targetCharacter.Name}");
        var courtRoomId = GetOptionalParameterValue<uint>(parameters, "court", 0);
        var courtRoom = TrialManager.Instance.CourtRooms.GetValueOrDefault(courtRoomId);
        switch (action)
        {
            case "list":
                if (courtRoom != null)
                {
                    var judge = courtRoom.JudgeSpawner?.SpawnedNpcs?.LastOrDefault().Value?.LastOrDefault();
                    SendMessage(messageOutput, $"Name: {courtRoom.Name} ({courtRoomId})");
                    SendMessage(messageOutput, $"Judge: {judge?.Transform.World.Position.ToString() ?? "none"} from {courtRoom.JudgeSpawner?.Position} (spawnerId: {courtRoom.JudgeSpawner?.Id})");
                    SendMessage(messageOutput, $"Defendant location: {courtRoom.Defendant}");
                    SendMessage(messageOutput, $"Jail location: {courtRoom.Jail}");
                    foreach (var (seatId, jurySeat) in courtRoom.JurySeats)
                    {
                        SendMessage(messageOutput, $"Seat {seatId}: {jurySeat.Transform.World.Position}");
                    }

                    if (courtRoom.CurrentTrial == null)
                    {
                        SendMessage(messageOutput, $"No ongoing trial");
                    }
                    else
                    {
                        SendMessage(messageOutput, $"Trial ongoing {courtRoom.CurrentTrial.Id} for {courtRoom.CurrentTrial.Defendant.Name}, Step: {courtRoom.CurrentTrial.Step}");
                    }
                }
                else
                {
                    SendColorMessage(messageOutput, Color.Red, $"Invalid courtroom id: {courtRoomId}.");
                }
                break;
            case "jail":
                if (courtRoom != null)
                {
                    SendMessage(messageOutput, $"Moving {targetCharacter.Name} to jail of {courtRoom.Name} ({courtRoomId})");
                    targetCharacter.ForceDismount();
                    targetCharacter.DisabledSetPosition = true;
                    if (!targetCharacter.Buffs.CheckBuff((uint)BuffConstants.ForciblyAwaitingTrial))
                        targetCharacter.Buffs.AddBuff((uint)BuffConstants.ForciblyAwaitingTrial, targetCharacter);
                    targetCharacter.SendPacket(new SCTeleportUnitPacket(0, 0, courtRoom.Jail.X, courtRoom.Jail.Y, courtRoom.Jail.Z, courtRoom.Jail.Yaw.DegToRad()));
                    /*
                    if (courtRoom.Region == CourtRoomRegion.Nuian)
                        targetCharacter.Buffs.AddBuff(BuffConstants.PrisonerNuian); 
                    if (courtRoom.Region == CourtRoomRegion.Haranyan)
                        targetCharacter.Buffs.AddBuff(BuffConstants.PrisonerHaranyan);
                    */ 
                }
                else
                {
                    SendColorMessage(messageOutput, Color.Red, $"Invalid courtroom id: {courtRoomId}.");
                }
                break;
            /*
            case "prison":
                if (courtRoom != null)
                {
                    SendMessage(messageOutput, $"Moving {targetCharacter.Name} to prison of {courtRoom.Name} ({courtRoomId})");
                    targetCharacter.ForceDismount();
                    targetCharacter.DisabledSetPosition = true;
                    //targetCharacter.Buffs.AddBuff((uint)BuffConstants.ForciblyAwaitingTrial, targetCharacter);
                    //targetCharacter.SendPacket(new SCTeleportUnitPacket(0, 0, courtRoom.Jail.X, courtRoom.Jail.Y, courtRoom.Jail.Z, courtRoom.Jail.Yaw.DegToRad()));
                    if (courtRoom.Region == CourtRoomRegion.Nuian && !targetCharacter.Buffs.CheckBuff((uint)BuffConstants.Prisoner_Nuian))
                        targetCharacter.Buffs.AddBuff((uint)BuffConstants.Prisoner_Nuian, targetCharacter);
                    if (courtRoom.Region == CourtRoomRegion.Haranyan && !targetCharacter.Buffs.CheckBuff((uint)BuffConstants.Prisoner_Haranyan))
                        targetCharacter.Buffs.AddBuff((uint)BuffConstants.Prisoner_Haranyan, targetCharacter);
                }
                else
                {
                    SendColorMessage(messageOutput, Color.Red, $"Invalid courtroom id: {courtRoomId}.");
                }
                break;
            */
        }
        // var trialId = GetOptionalParameterValue<uint>(parameters, "trial", 0);
        // targetCharacter.SendPacket(new SCInviteJuryPacket(defendantName, trialId));
    }
}
