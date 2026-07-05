using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Crime;

public class TrialData
{
    /// <summary>
    /// TrialId
    /// </summary>
    public uint Id { get; init; }

    public CourtRoomRegion CourtRegion { get; set; }
    public TrialCourtRoom CourtRoom { get; set; }
    public TrialStep Step { get; set; } = TrialStep.DefendantAwaitingTrial;
    public Character Defendant { get; set; }
    public uint DefendantId { get; set; }
    public string DefendantName { get; set; }
    public DateTime CurrentStepEndTime { get; set; } = DateTime.MinValue;
    public uint RemainingCurrentStepTime
    {
        get
        {
            return (uint)(CurrentStepEndTime - DateTime.UtcNow).TotalMilliseconds;
        }
    }

    public Dictionary<int, TrialJuryBox> Jury { get; init; } = [];
    public int JailTime { get; set; } = 15; // 15 minute default
    public List<CrimeEvent> EvidenceList { get; set; } = [];

    /// <summary>
    /// Called by arresting function to put the criminal into court jail to await trial 
    /// </summary>
    /// <param name="temporaryCourtRoom"></param>
    /// <param name="defendant"></param>
    public void EnterCourtJail(TrialCourtRoom temporaryCourtRoom, Character defendant)
    {
        CourtRegion = temporaryCourtRoom.Region;
        CourtRoom = temporaryCourtRoom; // This can be overwritten when actually going to court/jury
        Defendant = defendant;
        DefendantId = defendant.Id;
        DefendantName = defendant.Name;

        // Make sure criminal is alive
        defendant.Hp = Math.Max(1, defendant.Hp);
        defendant.BroadcastPacket(
            new SCCharacterResurrectedPacket(defendant.ObjId, defendant.Transform.World.Position.X,
                defendant.Transform.World.Position.Y, defendant.Transform.World.Position.Z,
                defendant.Transform.World.Rotation.Z), true);
        defendant.BroadcastPacket(
            new SCUnitPointsPacket(defendant.ObjId, defendant.Hp, defendant.Mp), true);
        defendant.PostUpdateCurrentHp(defendant, 0, defendant.Hp, KillReason.PvpEnemy);        

        // Teleport criminal to the local jail
        defendant.SendPacket(new SCTeleportUnitPacket(TeleportReason.Lockup, 0, temporaryCourtRoom.Jail.X, temporaryCourtRoom.Jail.Y, temporaryCourtRoom.Jail.Z, temporaryCourtRoom.Jail.Yaw.DegToRad()));

        // Haranya -> returnDistrict: 427, resurrectionDistrict: 68
        /*
        var returnDistrict = 427u;
        var resurrectionDistrict = 68u;
        defendant.SendPacket(new SCCharacterBoundPacket(defendant.Id, returnDistrict, resurrectionDistrict, false, 0, string.Empty, 0, Vector3.Zero, 0f, false));
        */

        // Send guilty/trial question to player
        // TODO: Not yet sure if this sends CrimePoint or CrimeRecord or just 50 of the threshold
        defendant.SendPacket(new SCAskImprisonOrTrialPacket((uint)defendant.CrimePoint, JailTime));
        
        defendant.SendPacket(new SCTrialWaitStatusPacket(0, JailTime * 60_000));
    }

    /// <summary>
    /// Calculates the default jail time (in minutes)
    /// </summary>
    public void CalculateJailTime()
    {
        // Info from an old reddit post from when the game was still rather new
        // https://www.reddit.com/r/archeage/comments/2h8wbf/how_are_prison_sentence_times_actually_calculated/
        // Minutes = (crimePoints / 5) * (1 + (Infamy / 1000))
        // Pirate = max 40
        // However this information seems to be wrong compared to what the client shows
        // Below is what I suspect are the values based on what the client returns from trial and error
        var defaultMinutes = 0;
        foreach (var crimeEvent in EvidenceList.ToList())
        {
            var thisEventScore = 0;
            switch (crimeEvent.CrimeKind)
            {
                case CrimeKind.None:
                    thisEventScore = 0;
                    break;
                case CrimeKind.Assault:
                    thisEventScore = 0;
                    break;
                case CrimeKind.Murder:
                    thisEventScore = 20;
                    break;
                case CrimeKind.Theft:
                    thisEventScore = 8;
                    break;
            }
            // There seems to be a level-difference penalty here as well
            // TODO: move this to the crime table so we can just grab it from there? Or is it from data at the time of trial?
            var victim = WorldManager.Instance.GetCharacterById(crimeEvent.Victim);
            if (victim?.Level < 30)
            {
                thisEventScore *= 10;
            }
            defaultMinutes += thisEventScore;
        }

        JailTime = defaultMinutes; // (Defendant.CrimePoint / 5);
        JailTime *= (1 + (Defendant.InfamyPoint / 1000));
        if (Defendant.Faction.Id == FactionsEnum.Pirate)
        {
            JailTime = 40; // Math.Min(40, Math.Max(JailTime, 15)); // Not correct, but good enough for now
        }

        // Store the Jail time in case the trial doesn't go through
        Defendant.OfflineGuiltyTime = JailTime;
        Defendant.OfflineGuiltyRegion = CourtRegion;
    }

    public void EnterCourtRoom(TrialCourtRoom courtRoom, Character defendant)
    {
        courtRoom.CurrentTrial = this;
        CourtRoom = courtRoom;

        // Make Jury seats
        Jury.Clear();
        for(var i = 1; i <= CourtRoom.JurySeats.Count; i++)
        {
            var seat = CourtRoom.JurySeats.GetValueOrDefault(i);
            if (seat == null)
            {
                // Failed to find seat doodad
                continue;
            }
            Jury.Add(i, new TrialJuryBox { CourtRoom = CourtRoom, Seat = seat, SeatId = i, ConfirmTestimony = false, SelectedSentence = -1});
        }
        DefendantId = defendant.Id;
        DefendantName = defendant.Name;

        // On Trial buff 
        defendant.Buffs.RemoveBuff((uint)BuffConstants.ForciblyAwaitingTrial);
        defendant.Buffs.AddBuff((uint)BuffConstants.Trial_Defendant, defendant);
        
        // Teleport defendant
        defendant.SendPacket(new SCTeleportUnitPacket(TeleportReason.Defendant, 0, CourtRoom.Defendant.X, CourtRoom.Defendant.Y, CourtRoom.Defendant.Z, CourtRoom.Defendant.Yaw.DegToRad()));

        // Summon defendant packet (starts trial cycle for client?)
        defendant.SendPacket(new SCSummonDefendantPacket(Id));
        courtRoom.TrialChatChannel.JoinChannel(defendant);

        // Jury Wait Status 
        defendant.SendPacket(new SCJuryWaitStatusPacket(0, Jury.Values.Count, JailTime * 60_000));

        // Change trial state
        // defendant.SendDebugMessage($"TrialStep.InitializeTrial - {Id}");
        Step = TrialStep.VerifyCriminalRecord;
        CurrentStepEndTime = DateTime.UtcNow.AddSeconds(TrialManager.TrialInitSeconds);
        defendant.SendPacket(new SCChangeTrialStatePacket(Id, (byte)Step, 0, RemainingCurrentStepTime));

        // Trial Info
        SendAllTrialRecords(defendant);

        // defendant.SendDebugMessage($"TrialStep.AwaitingJurySummons - {Id}");
        Step = TrialStep.AwaitingJurySummons;
        CurrentStepEndTime = DateTime.UtcNow.AddMinutes(TrialManager.AwaitingJurySummonsMinutes);
        // CurrentStepEndTime = DateTime.UtcNow.AddSeconds(10); // For debug
        defendant.SendPacket(new SCChangeTrialStatePacket(Id, (byte)Step, 0, RemainingCurrentStepTime));

        // Cannot Escape Buff
        defendant.Buffs.AddBuff((uint)BuffConstants.CannotEscapeBuff, defendant);

        // Force self-target
        defendant.SendPacket(new SCForceAttackSetPacket(defendant.ObjId, false));
        
        // Send Jury Invites
        SendJuryInvites();
    }

    /// <summary>
    /// Sends out invites to the first few people in the jury queue
    /// </summary>
    private void SendJuryInvites()
    {
        var juryOptionsList = TrialManager.Instance.GenerateJuryList(this);
        foreach (var player in juryOptionsList)
        {
            player.SendPacket(new SCInviteJuryPacket(DefendantName, Id));
        }
    }

    // Sends all trial and records info to target player
    private void SendAllTrialRecords(Character player)
    {
        if (player == null)
            return;
        try
        {
            // Trial Info
            player.SendPacket(new SCTrialInfoPacket(DefendantId, Defendant.CrimePoint,
                Defendant.ArrestCount, Defendant.AcceptGuiltyCount, Defendant.AcceptTrialCount,
                Defendant.NotGuiltyCount, Defendant.GuiltyCount,
                0, 0,
                Defendant.EvidenceReportedCount, Defendant.BotReportedCount));

            // Evidence List
            var evidenceList = CrimeManager.Instance.GetCrimesOfPlayer(DefendantId, false);
            var evidencePacketList = TrialManager.GenerateCrimeRecordsPacketList(Id, 0, evidenceList);
            foreach (var p in evidencePacketList)
            {
                player.SendPacket(p);
            }

            // Crime Data
            player.SendPacket(new SCCrimeDataPacket(DefendantId, DefendantName, Defendant.Race,
                (uint)Defendant.Faction.Id, Id, JailTime * 60_000,
                CourtRoom.JudgeSpawner?.SpawnedNpcs.Values.FirstOrDefault()?.FirstOrDefault()?.ObjId ?? 0));
        }
        catch (Exception)
        {
            // something is wrong?
        }
    }

    public bool SummonJuryMember(Character player)
    {
        // TODO: Verify the packet order with a real capture
        // Find an empty seat
        foreach (var (jurySeatId, juryEntry) in Jury)
        {
            if (juryEntry.JuryMember == null)
            {
                juryEntry.JuryMember = player;
                TrialManager.Instance.RemovePlayerFromQueue(juryEntry.JuryMember);

                // Save current position to return to after the trial (or if you disconnected)
                player.MainWorldPosition = player.Transform.CloneDetached(player);
                player.SendMessage($"Saving location {player.Transform.World.Position} -> {player.MainWorldPosition.World.Position}");
                player.SendPacket(new SCSummonJuryPacket(Id, CourtRoom.Id, jurySeatId));

                // Teleport the jury
                var pos = juryEntry.Seat.Transform.World.Position;
                player.SendPacket(new SCTeleportUnitPacket(TeleportReason.Jury, 0, pos.X, pos.Y, pos.Z, juryEntry.Seat.Transform.World.Rotation.Z));
                player.Transform.World.SetPosition(pos);
                player.SendMessage($"New location {player.Transform.World.Position}");
                
                // Seat them down
                player.SendPacket(new SCJuryBeSeatedPacket(CourtRegion == CourtRoomRegion.Haranyan, Id, (int)CourtRoom.Id, juryEntry.SeatId));

                CourtRoom.TrialChatChannel.JoinChannel(player);
                // TODO: Remove the hardcoded attachment and bound Ids by grabbing them from the chair's DoodadFuncAttachment
                /*
                var attachFuncsSelect = juryEntry.Seat.CurrentFuncs.Where(x => x.FuncType == "DoodadFuncAttachment").ToList();
                if (attachFuncsSelect.Count > 0)
                {
                    var groups = DoodadManager.Instance.GetDoodadFuncs(attachFuncsSelect.FirstOrDefault()?.GroupId ?? 0);
                    foreach (var doodadFunc in groups)
                    {
                        var skillId = doodadFunc.SkillId;
                        if (skillId > 0)
                        {
                            player.UseSkill(skillId, juryEntry.Seat);
                            break;
                        }
                    }
                }
                */

                // Court related buffs
                player.Buffs.AddBuff((uint)BuffConstants.CourtHouse, player); // Courthouse (this is normally already given by the AoE, but just to make sure)
                player.Buffs.AddBuff((uint)BuffConstants.Jury, player); // Jury (not sure if this even does anything)
                player.Buffs.AddBuff((uint)BuffConstants.CannotEscapeBuff, player); // Cannot escape (10s)

                // Bind Jury to their seat
                // Haranya -> returnDistrict: 427, resurrectionDistrict: 68
                // Not sure if this is related to a trial related, or this just happens to be near a recall point
                /*
                var returnDistrict = 427u;
                var resurrectionDistrict = 68u;
                player.SendPacket(new SCCharacterBoundPacket(player.Id, returnDistrict, resurrectionDistrict, false, 0, string.Empty, 0, Vector3.Zero, 0f, false));
                */

                /*
                var spot = juryEntry.Seat.Seat.LoadPassenger(player, juryEntry.Seat.ObjId, 1);
                player.Bonding = new BondDoodad(juryEntry.Seat, AttachPointKind.Passenger0, BondKind.BondChairSingle, 1, spot);
                player.BroadcastPacket(new SCBondDoodadPacket(player.ObjId, player.Bonding), true);
                player.Transform.StickyParent = juryEntry.Seat.Transform.StickyParent;
                player.Transform.Parent = juryEntry.Seat.Transform;
                */

                // Send them the trial info
                SendAllTrialRecords(player);
                var currentJuryCount = GetActiveJuryCount();
                SendPackets(new SCJuryWaitStatusPacket(currentJuryCount, Jury.Values.Count, JailTime * 60_000));

                // End this trial step if all seats taken by making it trigger the end-time sooner
                if (currentJuryCount >= Jury.Values.Count && Step == TrialStep.AwaitingJurySummons && CurrentStepEndTime > DateTime.UtcNow)
                    CurrentStepEndTime = DateTime.UtcNow;

                return true;
            }
        }
        // Could not find a seat for the jury member?
        return false;
    }

    /// <summary>
    /// Sends a packet to the defendant and all jury members
    /// </summary>
    /// <param name="packet"></param>
    public void SendPackets(GamePacket packet)
    {
        Defendant?.SendPacket(packet);
        foreach (var jury in Jury.Values)
        {
            jury?.JuryMember?.SendPacket(packet);
        }
        // TODO: Maybe include audience as well?
    }

    /// <summary>
    /// Mark all evidence in this trial case as archived or delete them depending on settings
    /// </summary>
    public void ArchiveEvidence()
    {
        var judgeTime = DateTime.UtcNow;
        foreach (var crimeEvent in EvidenceList)
        {
            if (AppConfiguration.Instance.Justice.KeepHistory)
            {
                CrimeManager.Instance.ArchiveEvidence(crimeEvent, judgeTime);
            }
            else
            {
                CrimeManager.Instance.RemoveEvidence(crimeEvent);
            }
        }
    }

    public int GetActiveJuryCount()
    {
        if (CourtRoom == null)
            return 0;
        var c = 0;
        foreach (var juryBox in Jury.Values)
        {
            if (juryBox.JuryMember is { IsOnline: true })
                c++;
        }
        return c;
    }

    /// <summary>
    /// Returns true if all jury members clicked "Give Verdict"
    /// </summary>
    /// <returns></returns>
    public bool AllJuryConfirmedCriminalRecords()
    {
        foreach (var juryValue in Jury.Values)
        {
            if (juryValue.JuryMember != null && juryValue.ConfirmTestimony == false)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if all jury members clicked a (not) guilty sentence time
    /// </summary>
    /// <returns></returns>
    public bool AllJurySelectedSentence()
    {
        foreach (var juryValue in Jury.Values)
        {
            if (juryValue.JuryMember != null && juryValue.SelectedSentence < 0)
                return false;
        }
        return true;
    }

    public void FinalizeVerdict()
    {
        var total = 0;
        var selectedCount = 0;
        var voteResults = new Dictionary<int, int>();
        foreach (var juryValue in Jury.Values)
        {
            if (juryValue.JuryMember != null)
            {
                total++;
                if (juryValue.SelectedSentence >= 0)
                {
                    if (!voteResults.ContainsKey(juryValue.SelectedSentence))
                        voteResults.Add(juryValue.SelectedSentence, 0);
                    
                    voteResults[juryValue.SelectedSentence]++;
                    selectedCount++;
                }
                juryValue.JuryMember?.Buffs.RemoveBuff((uint)BuffConstants.Jury);
            }
        }

        // TODO: Figure out how the times ojn the jury buttons are calculated
        // TODO: Calculate actual times by jury "average"


        var mostVoted = 3; // default guilty
        var mostVotedCount = 0;
        foreach (var (voteKey, voteCount) in voteResults)
        {
            if (voteCount > mostVotedCount)
            {
                mostVoted = voteKey;
                mostVotedCount = voteCount;
            }
        }

        var isGuilty = mostVoted > 1;
        // NG , 2 , 3 , 4 , 5 , 6
        // TODO: verify formula
        // Example: 0  ,  3  ,  8  ,  13  ,  17  ,  20
        // Rates:   0    1/3    1    5/3           9/4

        TrialSentenceResult sentenceType;
        var sentenceTime = (int)Math.Round(JailTime * 1f);
        switch (mostVoted)
        {
            case 0:
            case 1:
                // Not guilty
                sentenceType = TrialSentenceResult.NotGuilty;
                sentenceTime = 0;
                break;
            case 2:
                sentenceType = TrialSentenceResult.Guilty1;
                sentenceTime = (int)Math.Round(JailTime * 0.2f);
                break;
            case 3:
                sentenceType = TrialSentenceResult.Guilty2;
                sentenceTime = (int)Math.Round(JailTime * 0.5f);
                break;
            case 4:
                sentenceType = TrialSentenceResult.Guilty3;
                sentenceTime = (int)Math.Round(JailTime * 0.8f);
                break;
            case 5:
                sentenceType = TrialSentenceResult.Guilty4;
                sentenceTime = (int)Math.Round(JailTime * 1f);
                break;
            case 6:
                sentenceType = TrialSentenceResult.Guilty5;
                sentenceTime = (int)Math.Round(JailTime * 1.2f);
                break;
            default:
                // Should not happen
                sentenceType = TrialSentenceResult.NotGuilty;
                sentenceTime = 0;
                break;
        }

        SendPackets(new SCRulingStatusPacket(selectedCount, total, sentenceType, sentenceTime * 60_000));

        JailTime = sentenceTime;
        if (isGuilty)
        {
            TrialManager.Instance.ResultIsGuilty(Defendant, this, false);
        }
        else
        {
            TrialManager.Instance.ResultIsNotGuilty(Defendant, this);
        }
    }
}
