using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Crime;
using NLog;

namespace AAEmu.Game.Core.Managers;

// TODO: Handle/check/create these related errors
// ErrorMessageType.CannotExitWhileInTrial

public class TrialManager : Singleton<TrialManager>, ITrialManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public const int TrialInitSeconds = 10;
    public const int AwaitingJurySummonsMinutes = 2;
    private const int ConfirmCriminalRecordMinutes = 5;
    private const int ClosingStatementMinutes = 1;
    private const int JuryVerdictMinutes = 1;
    private const int EndTrialSeconds = 30;

    private readonly Lock _queueLock = new();
    private bool _loading;
    private bool _loaded;

    public Dictionary<uint, TrialCourtRoom> CourtRooms { get; } = [];
    private ConcurrentDictionary<uint, TrialData> Trials { get; } = [];

    /// <summary>
    /// PlayerIds of the Jury queues for various factions
    /// </summary>
    private Dictionary<FactionsEnum, List<uint>> JuryQueues { get; } = [];

    public CourtRoomRegion GetCourtRoomRegionByFaction(FactionsEnum faction)
    {
        return faction switch
        {
            FactionsEnum.NuiaAlliance => CourtRoomRegion.Nuian,
            FactionsEnum.HaranyaAlliance => CourtRoomRegion.Haranyan,
            _ => CourtRoomRegion.Invalid
        };
    }

    public void Load()
    {
        if (_loading || _loaded)
            return;
        _loading = true;
        // Default factions for jury, if you ever add more court factions, you will need to add them here.
        JuryQueues.Add(FactionsEnum.NuiaAlliance, []);
        JuryQueues.Add(FactionsEnum.HaranyaAlliance, []);

        foreach (var courtRoomConfig in AppConfiguration.Instance.Justice.CourtRooms)
        {
            var courtRoom = new TrialCourtRoom
            {
                Id = courtRoomConfig.Id,
                Region = GetCourtRoomRegionByFaction(courtRoomConfig.Faction),
                Faction = courtRoomConfig.Faction,
                Name = courtRoomConfig.Name,
                Defendant = courtRoomConfig.Defendant,
                Jail = courtRoomConfig.HoldingCell,
                CurrentTrial = null
            };
            // Seats and judge will be added after spawns
            CourtRooms.Add(courtRoom.Id, courtRoom);
            courtRoom.TrialChatChannel = ChatManager.Instance.GetTrialChat(courtRoom.Region);
        }

        TaskManager.Instance.Schedule(new TrialUpdateTask(), null, TimeSpan.FromSeconds(5));

        Logger.Info($"Loaded trials data");
        _loaded = true;
        _loading = false;
    }

    private bool CanAcceptTrialInvites(Character character)
    {
        // Needs to be online
        if (character == null || !character.IsOnline)
            return false;

        // Needs to have unlocked jury duty
        if (character.JuryPoint <= 0)
            return false;

        // Not sure about this one
        if (character.CrimePoint >= CrimeManager.WantedCrimePointThreshold)
            return false;

        // Cannot be a wanted or bot suspect
        if (character.Buffs.CheckBuffTag((uint)BuffConstants.TagOffender))
            return false;

        // No prisoners allowed
        if (character.Buffs.CheckBuffTag((uint)BuffConstants.TagPrisoner))
            return false;

        // Faction check is not done here and will prevent the player from actually getting in a queue if needed
        if (GetParticipatingTrial(character) != null)
            return false;

        return true;
    }

    /// <summary>
    /// Updates and re-orders the jury queue as needed
    /// Handles disconnected players and those that are not eligible
    /// </summary>
    public void UpdateJuryQueue()
    {
        List<uint> toRemove = [];
        lock (_queueLock)
        {
            // Add new characters to the list
            var characterList = WorldManager.Instance.MainWorld.GetAllCharacters();
            foreach (var character in characterList)
            {
                if (!CanAcceptTrialInvites(character))
                    continue;

                // Check if faction has a queue
                if (!JuryQueues.TryGetValue(character.Faction.MotherId, out var queue))
                    continue;

                // Add it to queue
                if (!queue.Contains(character.Id))
                {
                    queue.Add(character.Id);
                }
            }

            // Remove offline characters
            foreach (var (faction, queue) in JuryQueues)
            {
                foreach (var playerId in queue)
                {
                    var player = WorldManager.Instance.GetCharacterById(playerId);
                    if (!CanAcceptTrialInvites(player) || faction != player.Faction.MotherId)
                    {
                        toRemove.Add(playerId);
                    }
                }
            }
        }

        // Actually remove the playerIds from all the queues
        lock (_queueLock)
        {
            foreach (var playerId in toRemove)
            {
                foreach (var queue in JuryQueues.Values)
                {
                    queue.Remove(playerId);
                }
            }
        }
    }

    /// <summary>
    /// Basic handling of trial flow states
    /// </summary>
    private void UpdateTrialStates()
    {
        foreach (var trialData in Trials.Values.ToList())
        {
            // Waiting for empty courtroom when defendant decided to go on trial
            if (trialData.Step == TrialStep.DefendantAwaitingTrial && trialData.CurrentStepEndTime <= DateTime.UtcNow)
            {
                // Try to find a free courtroom
                var targetCourtRoom =
                    CourtRooms.Values.FirstOrDefault(x => x.Region == trialData.CourtRegion && x.CurrentTrial == null);
                if (targetCourtRoom is null)
                    continue;

                trialData.EnterCourtRoom(targetCourtRoom, trialData.Defendant);
            }

            if (trialData.Step is > TrialStep.AwaitingJurySummons and < TrialStep.TrialCancelled && trialData.GetActiveJuryCount() <= 0)
            {
                // If no jury left during a trial, auto-cancel it
                trialData.Step = TrialStep.TrialCancelled;
            }

            // If awaiting jury summons and there is nobody left in the queue, then immediately skip waiting for the others
            // This prevents the 2 minutes wait on very low population servers
            if (trialData.Step == TrialStep.AwaitingJurySummons && !QueueAvailable(trialData))
            {
                Logger.Debug($"TrialStep.AwaitingJurySummons skipping jury wait, nobody in queue - {trialData.Id}");
                trialData.CurrentStepEndTime = DateTime.UtcNow;
            }

            // Wait for jury end
            if (trialData.Step == TrialStep.AwaitingJurySummons && trialData.CurrentStepEndTime <= DateTime.UtcNow)
            {
                Logger.Debug($"TrialStep.ConfirmCriminalRecord - {trialData.Id}");
                trialData.Step = TrialStep.ConfirmCriminalRecord;
                trialData.CurrentStepEndTime =
                    DateTime.UtcNow.AddMinutes(ConfirmCriminalRecordMinutes);

                // TODO: find the correct packet and/or text Id to use here, 3.x uses SCChatToken
                trialData.SendPackets(new SCChatMessagePacket(ChatType.Judge, $"Begin the trial of {trialData.DefendantName}"));
                // trialData.SendPackets(new SCNpcChatMessagePacket(ChatType.Judge, trialData.CourtRoom.JudgeSpawner.SpawnedNpcs.Values.FirstOrDefault()?.FirstOrDefault() ?? null, null, 1, 113, trialData.DefendantName));

                // Update the step and send the new active jury count
                trialData.SendPackets(new SCChangeTrialStatePacket(trialData.Id, (byte)trialData.Step,
                    trialData.GetActiveJuryCount(), trialData.RemainingCurrentStepTime));
            }

            // Wait for "give verdict" by jury
            if (trialData.Step == TrialStep.ConfirmCriminalRecord && trialData.CurrentStepEndTime <= DateTime.UtcNow)
            {
                // Goto select verdict
                Logger.Debug($"TrialStep.ClosingStatement - {trialData.Id}");
                trialData.Step = TrialStep.ClosingStatement;
                trialData.CurrentStepEndTime = DateTime.UtcNow.AddMinutes(ClosingStatementMinutes);

                // Update the step
                trialData.SendPackets(new SCChangeTrialStatePacket(trialData.Id, (byte)trialData.Step, trialData.GetActiveJuryCount(), trialData.RemainingCurrentStepTime));
            }

            // Wait for Closing statement of the defendant
            if (trialData.Step == TrialStep.ClosingStatement && trialData.CurrentStepEndTime <= DateTime.UtcNow)
            {
                // Goto select verdict
                Logger.Debug($"TrialStep.JuryVerdict - {trialData.Id}");
                trialData.Step = TrialStep.JuryVerdict;
                trialData.CurrentStepEndTime = DateTime.UtcNow.AddMinutes(ClosingStatementMinutes);

                // Update the step
                trialData.SendPackets(new SCChangeTrialStatePacket(trialData.Id, (byte)trialData.Step, trialData.GetActiveJuryCount(), trialData.RemainingCurrentStepTime));

                // Update jury verdict panel to default
                trialData.Defendant?.SendPacket(new SCRulingStatusPacket(0, trialData.GetActiveJuryCount(), 0, 0));
            }

            if (trialData.Step == TrialStep.JuryVerdict && trialData.CurrentStepEndTime <= DateTime.UtcNow)
            {
                // Goto trial result
                Logger.Debug($"TrialStep.CalculateSentence - {trialData.Id}");
                trialData.Step = TrialStep.EndTrial;
                trialData.CurrentStepEndTime = DateTime.UtcNow.AddMinutes(JuryVerdictMinutes);

                // Update the step
                trialData.SendPackets(new SCChangeTrialStatePacket(trialData.Id, (byte)trialData.Step, trialData.GetActiveJuryCount(), trialData.RemainingCurrentStepTime));

                trialData.FinalizeVerdict();
            }

            if (trialData.Step == TrialStep.EndTrial && trialData.CurrentStepEndTime <= DateTime.UtcNow)
            {
                // Clean up trial
                trialData.SendPackets(new SCRulingClosedPacket());

                Logger.Debug($"TrialStep.Cleanup - {trialData.Id}");
                trialData.Step = TrialStep.Cleanup;
                trialData.CurrentStepEndTime = DateTime.MaxValue;
                trialData.CourtRoom.TrialChatChannel.LeaveChannel(trialData.Defendant); // Remove defendant from chat if not already so

                foreach (var juryEntry in trialData.Jury.Values)
                {
                    if (juryEntry.JuryMember != null)
                    {
                        // Teleport the jury back to their original place
                        if (AppConfiguration.Instance.Justice.AllowJuryEscape && !juryEntry.JuryMember.Buffs.CheckBuff((uint)BuffConstants.CourtHouse))
                        {
                            // Jury is allowed to escape, only cancel the MainWorldPosition setting
                            juryEntry.JuryMember.MainWorldPosition = null;
                        }
                        else
                        {
                            var pos = juryEntry.JuryMember.MainWorldPosition?.World.Position ??
                                      juryEntry.JuryMember.Transform.World.Position;
                            
                            Logger.Debug($"Returning {juryEntry.JuryMember.Name} to {pos} <- {juryEntry.JuryMember.Transform.World.Position}");

                            juryEntry.JuryMember.ForceDismount(); // forces them off their seat
                            juryEntry.JuryMember.DisabledSetPosition = true;
                            Logger.Debug($"Returning jury member {juryEntry.JuryMember.Name} to their old position {pos}");
                            
                            juryEntry.JuryMember.SendPacket(new SCTeleportUnitPacket(TeleportReason.Jury, 0, pos.X,
                                pos.Y, pos.Z, juryEntry.Seat.Transform.World.Rotation.Z));
                            // juryEntry.JuryMember.Transform.World.SetPosition(pos);
                            
                            juryEntry.JuryMember.MainWorldPosition = null;
                            juryEntry.JuryMember.Buffs.RemoveBuff((uint)BuffConstants.CourtHouse);
                            juryEntry.JuryMember.Buffs.RemoveBuff((uint)BuffConstants.Jury);
                            Logger.Debug($"{juryEntry.JuryMember.Name} back at {juryEntry.JuryMember.Transform.World.Position}");

                            // Update trials attended count
                            juryEntry.JuryMember.JuryPoint++;
                        }
                        trialData.CourtRoom.TrialChatChannel.LeaveChannel(juryEntry.JuryMember); // Remove Jury from courtroom chat
                    }
                }
            }

            if (trialData.Step == TrialStep.PleadGuilty)
            {
                trialData.SendPackets(new SCRulingClosedPacket());
                Logger.Debug($"TrialStep.Cleanup from PleadGuilty - {trialData.Id}");
                trialData.Step = TrialStep.Cleanup;
                trialData.CurrentStepEndTime = DateTime.MaxValue;
            }

            if (trialData.Step == TrialStep.TrialCancelled)
            {
                ResultIsGuilty(trialData.Defendant, trialData, false);
                trialData.SendPackets(new SCRulingClosedPacket());
                Logger.Debug($"TrialStep.TrialCancelled - {trialData.Id}");
                trialData.Step = TrialStep.Cleanup;
                trialData.CurrentStepEndTime = DateTime.MaxValue;
            }

            // Delete trial when done
            if (trialData.Step == TrialStep.Cleanup)
            {
                // Free up the courtroom (if used)
                trialData.Step = TrialStep.Invalid;
                trialData.CourtRoom?.CurrentTrial = null;
                Logger.Debug($"TrialStep.Cleanup - {trialData.Id}");
                if (!Trials.Remove(trialData.Id, out _))
                {
                    Logger.Warn($"Failed to remove Trial {trialData.Id}");
                }
            }
        }
    }

    /// <summary>
    /// Checks if there are still people in the queue pool for this trial's faction
    /// </summary>
    /// <param name="trial"></param>
    /// <returns></returns>
    private bool QueueAvailable(TrialData trial)
    {
        if (!JuryQueues.TryGetValue(trial.CourtRoom.Faction, out var queue))
            return false;
        return queue.Count > 0;
    }

    /// <summary>
    /// Returns character's position in the jury queue by sending them the related packet
    /// </summary>
    /// <param name="player"></param>
    public void GetJuryQueueForPlayer(Character player)
    {
        if (!JuryQueues.TryGetValue(player.Faction.MotherId, out var queue))
            return;
        var pos = queue.IndexOf(player.Id);
        if (pos >= 0)
        {
            pos++;
            player.SendPacket(new SCJuryWaitingNumberPacket(pos));
        }
    }

    /// <summary>
    /// Removes a player from all jury queues
    /// </summary>
    /// <param name="player"></param>
    public void RemovePlayerFromQueue(Character player)
    {
        foreach (var juryQueuesValue in JuryQueues.Values)
        {
            juryQueuesValue.Remove(player.Id);
        }
    }

    /// <summary>
    /// Generates a list of elegible players for jury duty
    /// </summary>
    /// <param name="trial"></param>
    /// <param name="maxCount"></param>
    /// <returns></returns>
    public List<Character> GenerateJuryList(TrialData trial, int maxCount = 10)
    {
        var res = new List<Character>();
        if (!JuryQueues.TryGetValue(trial.CourtRoom.Faction, out var juryFactionQueue))
            return res;
        foreach (var playerId in juryFactionQueue)
        {
            var player = WorldManager.Instance.GetCharacterById(playerId);
            if (player == null || !player.IsOnline)
                continue;
            if (player.Id == trial.DefendantId) // don't invite self
                continue;
            res.Add(player);
            if (res.Count >= maxCount)
                break;
        }

        return res;
    }

    /// <summary>
    /// Handles the reply for a trial invite
    /// </summary>
    /// <param name="player"></param>
    /// <param name="accept"></param>
    /// <param name="trialId"></param>
    /// <returns></returns>
    public bool ProcessTrialInviteReply(Character player, bool accept, uint trialId)
    {
        if (player == null)
            return false;

        if (!Trials.TryGetValue(trialId, out var trial))
        {
            // Replied too late, don't remove from queue
            Logger.Debug(
                $"{player.Name} sent a jury invite reply for a non-existing trial ({trialId}). Possibly already concluded.");
            player.SendErrorMessage(ErrorMessageType.TrialsAlreadyClosed);
            return false;
        }

        if (!JuryQueues.TryGetValue(player.Faction.MotherId, out var queue))
        {
            player.SendErrorMessage(ErrorMessageType.TrialsJuryFull);
            SusManager.Instance.LogActivity(SusManager.CategoryCheating, player,
                "Player is trying to join as jury while not in an eligible faction");
            return false; // Not in queue
        }

        if (!queue.Contains(player.Id))
        {
            player.SendErrorMessage(ErrorMessageType.TrialsJuryFull);
            SusManager.Instance.LogActivity(SusManager.CategoryCheating, player,
                "Player is trying to join as jury while not in an the jury queue");
            return false; // Not in queue
        }

        if (!accept)
        {
            queue.Remove(player.Id); // Remove current queue position if manually declined
            return false;
        }

        // Verify if we are not already part of another trial
        if (Trials.Values.Any(x => x.Jury.Values.Any(j => j.JuryMember == player)))
        {
            // Not the correct error, but it happens if two trials start at the same time, and you get invites for both
            player.SendErrorMessage(ErrorMessageType.TrialsJuryFull);
            return false;
        }

        if (player.ParentWorld.Id != WorldManager.DefaultInstanceId)
        {
            // Only allow joining from within the main_world
            player.SendErrorMessage(ErrorMessageType.CannotJoinTrialFromInstantZone);
            return false;
        }

        if (trial.Step > TrialStep.AwaitingJurySummons)
        {
            player.SendErrorMessage(ErrorMessageType.TrialsCannotJoinAfterStart);
            return false;
        }

        // Send to courtroom
        var res = trial.SummonJuryMember(player);
        if (!res)
        {
            player.SendErrorMessage(ErrorMessageType.TrialsJuryFull);
        }

        return res;
    }

    /// <summary>
    /// Called every few seconds
    /// </summary>
    public void UpdateTick()
    {
        UpdateJuryQueue();
        UpdateTrialStates();
    }


    /// <summary>
    /// Call this when spawning the initial world Doodads and NPCs
    /// </summary>
    /// <param name="world"></param>
    public void InitializeDoodads(WorldInstance world)
    {
        var justiceDoodads = world.SpawnManager.GetSpecialDoodadSpawners("justice.");

        foreach (var (courtRoomId, trialCourtRoom) in CourtRooms)
        {
            foreach (var doodadSpawner in justiceDoodads)
            {
                // Jury seats
                for (var seatId = 1; seatId <= 5; seatId++)
                {
                    if (doodadSpawner.SpecialLink == $"justice.{courtRoomId}.seat.{seatId}")
                    {
                        trialCourtRoom.JurySeats.Add(seatId, doodadSpawner.Last);
                    }
                }

                // Audience seats
                if (doodadSpawner.SpecialLink == $"justice.{courtRoomId}.spectator")
                {
                    trialCourtRoom.AudienceSeats.Add(doodadSpawner.Last);
                }
            }
        }
    }

    /// <summary>
    /// Call this when spawning the initial world Doodads and NPCs
    /// </summary>
    /// <param name="world"></param>
    public void InitializeNpc(WorldInstance world)
    {
        var justiceNpcs = world.SpawnManager.GetSpecialNpcSpawners("justice.");

        foreach (var (courtRoomId, trialCourtRoom) in CourtRooms)
        {
            // NPCs
            foreach (var npcSpawner in justiceNpcs)
            {
                // Judge
                if (npcSpawner.SpecialLink == $"justice.{courtRoomId}.judge" && trialCourtRoom.JudgeSpawner == null)
                {
                    trialCourtRoom.JudgeSpawner = npcSpawner;
                    break;
                }
            }
        }
    }

    public void HandlePlayerLogin(Character player)
    {
        lock (_queueLock)
        {
            if (!CanAcceptTrialInvites(player))
                return;
            if (!JuryQueues.TryGetValue(player.Faction.MotherId, out var queue))
                return;
            if (!queue.Contains(player.Id))
                queue.Add(player.Id);
            // GetJuryQueueForPlayer(player);
        }

        if (player.OfflineGuiltyTime > 0)
        {
            // Not the Best way to handle this, but create a temporary trial and immediately auto-plead guilty
            var tempTrial = ArrestCriminal(player, null);
            if (tempTrial != null)
            {
                ResultIsGuilty(player, tempTrial, false);
            }
            else
            {
                Logger.Error($"Failed to handle justice for skipped trial of {player.Name} ({player.Id})! Time: {player.OfflineGuiltyTime}, Region: {player.OfflineGuiltyRegion}");
            }
        }
    }

    /// <summary>
    /// Handles the functions and packets that need to happen if a wanted criminal gets killed by a player
    /// </summary>
    /// <param name="criminal"></param>
    /// <param name="arrestor"></param>
    public TrialData ArrestCriminal(Character criminal, Character arrestor)
    {
        // TODO: Implement better support for player nations
        var criminalCourtRegion = GetCourtRoomRegionByFaction(criminal.Faction.MotherId);
        var arrestorCourtRegion = arrestor != null ? GetCourtRoomRegionByFaction(arrestor.Faction.MotherId) : criminal.OfflineGuiltyRegion;
        if (arrestor != null && criminalCourtRegion == CourtRoomRegion.Invalid && arrestorCourtRegion == CourtRoomRegion.Invalid)
        {
            // Likely both pirates, ignore
            Logger.Debug($"ArrestCriminal: {arrestor.Name} cannot arrest {criminal.Name}, both fall outside of justice system");
            return null;
        }

        // If criminal doesn't have a court region, use the arrestor's region instead
        // TODO: Verify if this is retail behaviour, or if it uses the original nation of the player
        if (criminalCourtRegion == CourtRoomRegion.Invalid)
        {
            criminalCourtRegion = arrestorCourtRegion;
        }

        // Find a courtroom to use
        var tempCourtRoom = CourtRooms.Values.FirstOrDefault(c => c.Region == criminalCourtRegion);
        if (tempCourtRoom == null)
        {
            Logger.Warn($"Failed to find a court room for {criminal.Name}");
            return null;
        }

        // Update counter
        if (arrestor != null)
        {
            criminal.ArrestCount++;
        }

        // Create Trial case
        var trialData = CreateTrialCase(criminal);
        if (trialData == null)
        {
            Logger.Warn($"Failed to create a court case for {criminal.Name}");
            return null;
        }

        // Notify nearby players
        if (arrestor != null)
            criminal.BroadcastPacket(new SCCriminalArrestedPacket(criminal.ObjId, criminal.Name, arrestor.Name), true);

        // Summon criminal to court jail
        trialData.EnterCourtJail(tempCourtRoom, criminal);
        return trialData;
    }

    /// <summary>
    /// Creates a trial case to be used
    /// </summary>
    /// <param name="defendant"></param>
    /// <returns></returns>
    private TrialData CreateTrialCase(Character defendant)
    {
        var trialData = new TrialData()
        {
            DefendantId = defendant.Id,
            DefendantName = defendant.Name,
            Id = TrialIdManager.Instance.GetNextId(),
            Step = TrialStep.DefendantAwaitingTrial,
            CurrentStepEndTime = DateTime.MaxValue,
            Defendant = defendant
        };
        Logger.Debug($"TrialStep.DefendantAwaitingTrial - {trialData.Id}");
        trialData.EvidenceList = CrimeManager.Instance.GetCrimesOfPlayer(defendant.Id, false);
        trialData.CalculateJailTime();
        var added = Trials.TryAdd(trialData.Id, trialData);
        return added ? trialData : null;
    }

    /// <summary>
    /// Generates a list of packets that contains the evidence data for trials
    /// </summary>
    /// <param name="trialId"></param>
    /// <param name="unk1"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    public static List<SCCrimeRecordsPacket> GenerateCrimeRecordsPacketList(uint trialId, uint unk1, List<CrimeEvent> events)
    {
        var res = new List<SCCrimeRecordsPacket>();

        if (events.Count == 0)
            return res;

        var start = 0;
        while (start < events.Count)
        {
            var rest = Math.Min(events.Count - start, 5);
            var eventBlocks = events.Slice(start, rest);
            res.Add(new SCCrimeRecordsPacket(trialId, unk1, events.Count, rest, eventBlocks));
            start += 5;
        }

        return res;
    }

    private TrialData GetTrialCase(uint defendantId)
    {
        return Trials.Values.FirstOrDefault(x => x.DefendantId == defendantId);
    }

    public void AddNewEvidence(CrimeEvent newEvent)
    {
        var relatedTrial = GetTrialCase(newEvent.Criminal);
        if (relatedTrial == null)
            return;
        relatedTrial.EvidenceList.Add(newEvent);
        relatedTrial.CalculateJailTime();
        relatedTrial.SendPackets(new SCCrimeRecordsPacket(relatedTrial.Id, 0, relatedTrial.EvidenceList.Count, 1,
            new[] { newEvent }));
    }

    public void ReplyImprisonOrTrial(Character defendant, bool requestTrial)
    {
        var trial = GetTrialCase(defendant.Id);
        if (trial is null)
        {
            Logger.Warn(
                $"{defendant.Name} ({defendant.Id}) sent a CSReplyImprisonOrTrialPacket without being on trial. requestTrial={requestTrial}");
            return;
        }

        if (!requestTrial)
        {
            // Directly to jail with trial.JailTime time
            ResultIsGuilty(defendant, trial, true);
            return;
        }

        // If not started yet, end the initial 2 minutes waiting time for an answer
        if (trial.Step == TrialStep.DefendantAwaitingTrial)
        {
            // End timer now
            defendant.AcceptTrialCount++; // TODO: How to handle if we don't respond
            trial.CurrentStepEndTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Sends the defendant to actual jail
    /// </summary>
    /// <param name="defendant"></param>
    /// <param name="trial"></param>
    /// <param name="pleadGuilty"></param>
    public void ResultIsGuilty(Character defendant, TrialData trial, bool pleadGuilty)
    {
        if (trial == null)
        {
            Logger.Warn($"Trial is null for {defendant}");
            return;
        }
        Logger.Debug($"TrialStep.EndTrial - {trial.Id}");
        trial.Step = pleadGuilty ? TrialStep.PleadGuilty : TrialStep.EndTrial;
        trial.CurrentStepEndTime = DateTime.UtcNow.AddSeconds(EndTrialSeconds);
        trial.SendPackets(new SCChangeTrialStatePacket(trial.Id, (byte)trial.Step, trial.GetActiveJuryCount(), trial.RemainingCurrentStepTime));
        trial.CourtRoom.TrialChatChannel.LeaveChannel(defendant);

        // Find jail to go to
        var targetJail = AppConfiguration.Instance.Justice.Jails.FirstOrDefault(x => x.Faction == trial.CourtRoom.Faction);
        if (targetJail == null)
        {
            Logger.Error($"Could not find a jail location for Court: {trial.CourtRoom.Name}, Defendant: {defendant?.Name}");
            return;
        }

        // Update evidence
        trial.ArchiveEvidence();

        // Update crime points
        var crimeCount = defendant.CrimePoint;
        defendant.OfflineGuiltyTime = 0;
        defendant.OfflineGuiltyRegion = CourtRoomRegion.Invalid;
        defendant.CrimePoint -= crimeCount;
        // Update counters
        if (pleadGuilty)
        {
            defendant.AcceptGuiltyCount++;
        }
        else
        {
            defendant.GuiltyCount++;
        }
        defendant.SendPacket(new SCCrimeChangedPacket(crimeCount, defendant.CrimePoint, defendant.InfamyPoint,
            defendant.GetCrimeState()));

        // Prisoner buff
        var jailTimeMs = trial.JailTime * 60_000;
        jailTimeMs = Math.Max(jailTimeMs, 10_000); // Make it minimum 10 seconds, having 0 here will make it use the default 30 minutes instead
        switch (trial.CourtRegion)
        {
            case CourtRoomRegion.Nuian:
                defendant.Buffs.AddBuff((uint)BuffConstants.Prisoner_Nuian, defendant, jailTimeMs);
                break;
            case CourtRoomRegion.Haranyan:
                defendant.Buffs.AddBuff((uint)BuffConstants.Prisoner_Haranyan, defendant, jailTimeMs);
                break;
            default:
                Logger.Error(
                    $"ResultIsGuilty - Invalid court region? {trial.CourtRegion}, defendant: {trial.DefendantName}");
                break;
        }

        // Teleport
        defendant.DisabledSetPosition = true;
        defendant.SendPacket(new SCTeleportUnitPacket(TeleportReason.Jail, 0, targetJail.Pos.X, targetJail.Pos.Y, targetJail.Pos.Z, targetJail.Pos.Yaw));
        defendant.Buffs.RemoveBuff((uint)BuffConstants.Trial_Defendant);
    }

    /// <summary>
    /// Frees the defendant
    /// </summary>
    /// <param name="defendant"></param>
    /// <param name="trial"></param>
    public void ResultIsNotGuilty(Character defendant, TrialData trial)
    {
        Logger.Debug($"TrialStep.EndTrial - {trial.Id}");
        trial.Step = TrialStep.EndTrial;
        trial.CurrentStepEndTime = DateTime.UtcNow.AddSeconds(EndTrialSeconds);
        trial.SendPackets(new SCChangeTrialStatePacket(trial.Id, (byte)trial.Step, trial.GetActiveJuryCount(), trial.RemainingCurrentStepTime));

        // Update evidence
        trial.ArchiveEvidence();

        // Update crime points
        var crimeCount = defendant.CrimePoint;
        defendant.OfflineGuiltyTime = 0;
        defendant.OfflineGuiltyRegion = CourtRoomRegion.Invalid;
        defendant.NotGuiltyCount++;
        defendant.CrimePoint -= crimeCount;
        defendant.InfamyPoint -= crimeCount;
        defendant.SendPacket(new SCCrimeChangedPacket(crimeCount, defendant.CrimePoint, defendant.InfamyPoint, defendant.GetCrimeState()));

        defendant.Buffs.RemoveBuffs(BuffKind.Good, 1, (uint)BuffConstants.TagPrisoner);
        defendant.Buffs.RemoveBuff((uint)BuffConstants.Trial_Defendant);

        if (defendant.InfamyPoint <= 0 && defendant.Faction.Id == FactionsEnum.Pirate)
        {
            // Return to original faction.
            var defaultFactionId = CharacterManager.Instance.GetTemplate(defendant.Race, defendant.Gender).FactionId;
            defendant.SetFaction(defaultFactionId);
        }
    }

    public TrialData GetTrial(uint trialId)
    {
        return Trials.GetValueOrDefault(trialId);
    }

    /// <summary>
    /// Marks a jury member as having confirmed the criminal record
    /// </summary>
    /// <param name="juryMember"></param>
    /// <param name="trial"></param>
    /// <param name="juryId"></param>
    public static void JuryEndTestimony(Character juryMember, TrialData trial, int juryId)
    {
        if (trial.Step != TrialStep.ConfirmCriminalRecord)
        {
            Logger.Warn($"JuryEndTestimony by {juryMember.Name} for trial {trial.Id} with seatId {juryId} was confirmed outside of the correct trial step");
            return;
        }
        if (!trial.Jury.TryGetValue(juryId, out var jury) || jury.SeatId != juryId)
        {
            Logger.Warn($"JuryEndTestimony by {juryMember.Name} seems to be invalid for trial {trial.Id} with seatId {juryId}");
            return; // ignore if invalid
        }

        jury.ConfirmTestimony = true;

        // Update totals
        var count = 0;
        var total = 0;
        foreach (var juryValue in trial.Jury.Values)
        {
            if (juryValue.JuryMember != null)
            {
                total++;
                if (juryValue.ConfirmTestimony)
                    count++;
            }
        }
        trial.SendPackets(new SCChangeJuryOKCountPacket(count, total));

        // Mark current step for ending if all have confirmed
        if (trial.AllJuryConfirmedCriminalRecords())
        {
            // TODO: this skip to the next step should technically be instance
            trial.CurrentStepEndTime = DateTime.UtcNow;
        }
    }

    public static void JuryVerdict(Character juryMember, TrialData trial, int juryId, byte sentence)
    {
        if (trial.Step != TrialStep.JuryVerdict)
        {
            Logger.Warn($"JuryVerdict by {juryMember.Name} for trial {trial.Id} with seatId {juryId} was confirmed outside of the correct trial step");
            return;
        }
        if (!trial.Jury.TryGetValue(juryId, out var jury) || jury.SeatId != juryId)
        {
            Logger.Warn($"JuryVerdict by {juryMember.Name} seems to be invalid for trial {trial.Id} with seatId {juryId}");
            return; // ignore if invalid
        }

        if (jury.JuryMember.Id != juryMember.Id)
        {
            Logger.Warn($"Jury Verdict Seat does not match the expected player");
            return;
        }

        jury.SelectedSentence = sentence;
        
        var count = 0;
        var total = 0;
        foreach (var juryValue in trial.Jury.Values)
        {
            if (juryValue.JuryMember != null)
            {
                total++;
                if (juryValue.SelectedSentence >= 0)
                    count++;
            }
        }

        // Send updated voted count
        var resultPacket = new SCRulingStatusPacket(count, total, 0, 0);
        trial.Defendant?.SendPacket(resultPacket);
        foreach (var juryValue in trial.Jury.Values)
        {
            if (juryValue.SelectedSentence >= 0)
                juryValue.JuryMember?.SendPacket(resultPacket);
        }

        // Mark current step for ending if all have confirmed
        if (trial.AllJurySelectedSentence())
        {
            trial.CurrentStepEndTime = DateTime.UtcNow;
        }
    }

    // Defendant declined a closing statement
    public static void SkipFinalStatementReply(Character defendant, TrialData trial)
    {
        if (trial.Defendant != defendant)
            return;
        if (trial.Step != TrialStep.ClosingStatement)
            return;

        trial.CurrentStepEndTime = DateTime.UtcNow;
    }

    public bool IsPlayerInCourt(uint playerId)
    {
        foreach (var trialData in Trials.Values)
        {
            if (trialData.DefendantId == playerId)
                return true;
            foreach (var jury in trialData.Jury.Values)
            {
                if (jury.JuryMember?.Id == playerId)
                    return true;
            }
        }
        return false;
    }

    public void JoinTrialAudience(Character player, uint doodadTemplateId)
    {
        // Try to find what courtroom it belongs too.
        // Technically each courtroom has its own doodad version, but we will be doing a bit more checks instead of
        // blindly trusting it.
        foreach (var trialCourtRoom in CourtRooms.Values)
        {
            if (trialCourtRoom.AudienceMembers.Contains(player))
            {
                // Already in this audience
                return;
            }
            foreach (var audienceSeat in trialCourtRoom.AudienceSeats)
            {
                if (audienceSeat.TemplateId != doodadTemplateId)
                {
                    continue;
                }

                var dist = player.GetDistanceTo(audienceSeat);
                if (!(dist <= 5f)) // 5m seems a fair enough check
                {
                    continue;
                }

                // Found a matching seat type
                trialCourtRoom.AudienceMembers.Add(player);
                player.BroadcastPacket(new SCTrialAudienceJoinedPacket(trialCourtRoom.CurrentTrial?.Id ?? 0, player.ObjId, player.Name), true);
                trialCourtRoom.TrialChatChannel.JoinChannel(player);
                return;
            }
        }
        // Was not able to find a valid nearby seat.
        // Are we hacking here ?
        var error = $"{player?.Name ?? "unknown"} tried to join courtroom audience while not being near a seat";
        Logger.Warn(error);
        SusManager.Instance.LogActivity(SusManager.CategoryCheating, player?.AccountId ?? 0, player?.Id ?? 0, player?.Transform.ZoneId ?? 0, player?.Transform.World.Position ?? Vector3.Zero,error);
    }

    public void LeaveTrialAudience(Character player)
    {
        foreach (var trialCourtRoom in CourtRooms.Values)
        {
            if (trialCourtRoom.AudienceMembers.Contains(player))
            {
                trialCourtRoom.AudienceMembers.Remove(player);
                player.BroadcastPacket(new SCTrialAudienceLeftPacket(player.ObjId, player.Name), true);
                trialCourtRoom.TrialChatChannel.LeaveChannel(player);
                return;
            }
        }
        Logger.Debug($"{player?.Name ?? "unknown"} tried to leave the courtroom audience but was not part of it");
    }

    public TrialData GetParticipatingTrial(Character player)
    {
        foreach (var courtRoom in CourtRooms.Values)
        {
            if (courtRoom.CurrentTrial == null)
            {
                continue;
            }

            if (courtRoom.CurrentTrial.Defendant == player)
            {
                return courtRoom.CurrentTrial;
            }
            foreach (var juryValue in courtRoom.CurrentTrial.Jury.Values)
            {
                if (juryValue.JuryMember == player)
                {
                    return courtRoom.CurrentTrial;
                }
            }
            foreach (var audience in courtRoom.AudienceMembers)
            {
                if (audience == player)
                {
                    return courtRoom.CurrentTrial;
                }
            }
        }
        return null;
    }
}
