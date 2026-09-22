using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.Justice;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Trials. A defendant who chooses to be tried waits at their courthouse while the standby queue is
/// invited in waiting-number order; the trial then runs the client's own phases - the bench gathers,
/// the bench reads the record, the defendant's final statement, the bench votes - and the verdict
/// either frees the defendant or hands them the sentence through the same jail path the arrest flow
/// uses. The defendant can end it early by admitting guilt, and either way the court's windows are
/// closed and everybody who was pulled in is released.
/// </summary>
public class TrialManager : Singleton<TrialManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// How many jurors a bench holds: the client's own seat table carries five chairs per bank per
    /// courtroom, and a trial uses the floor bank.
    /// </summary>
    public const int RequiredJurors = TrialSeatRules.SeatsPerBank;

    /// <summary>The player trial's type as SCTrialInfo and the audience join carry it (0 = player trial).</summary>
    public const uint PlayerTrialType = 0;

    /// <summary>
    /// The record total that means "there is no case file yet" to the client's trial window; it keeps
    /// an empty crime list from opening while the bench is still gathering.
    /// </summary>
    public const uint NoCaseFileYet = unchecked((uint)-1);

    private readonly Dictionary<ulong, Trial> _trials = [];
    private readonly Dictionary<uint, ulong> _audienceTrial = [];
    private readonly Dictionary<uint, ulong> _jurorTrial = [];

    /// <summary>The standby queue per courtroom, in waiting-number order.</summary>
    private readonly List<uint>[] _standby = [[], []];

    /// <summary>Which case is being heard in each courtroom - a courthouse hears one trial at a time.</summary>
    private readonly ulong?[] _courtTrial = [null, null];

    private ulong _nextTrialId = 1;

    /// <summary>
    /// Opens a trial for the defendant. The case file is opened first and the bench is then given its
    /// gathering window; the trial proper starts with whoever took a seat. A courthouse hears one case
    /// at a time, so a second defendant queues behind the one already standing there - their wait
    /// dialog is the court-order one until the room is free.
    /// </summary>
    public Trial StartTrial(Character defendant)
    {
        if (defendant == null)
            return null;

        if (GetLiveTrialOf(defendant.Id) is { } existing)
        {
            Logger.Warn($"Trial {existing.Id}: {defendant.Name} is already on trial - not opening another");
            return null;
        }

        var court = CourtOf(defendant);
        var trial = new Trial
        {
            Id = _nextTrialId++,
            DefendantId = defendant.Id,
            DefendantName = defendant.Name,
            Court = court,
            CrimePoint = Math.Max(0, (int)defendant.CrimePoint)
        };

        // The case file is what the defendant is being tried for right now. A crime reported while the
        // bench is sitting is a matter for the next case: it must not appear on a sheet read later, and
        // the verdict must not close it.
        trial.OpenCaseFile(CrimeManager.Instance.GetCrimesOfPlayer(defendant.Id).Select(c => c.Id));

        _trials[trial.Id] = trial;

        // The client learns which side of the case it is on from the summon packets: the defendant's
        // is what arms his wait window, his early-guilty plea and the final-statement dialog.
        defendant.SendPacket(new SCSummonDefendantPacket(trial.Id));

        // Until the bench convenes there is no case file to read, and the court's own "no case file"
        // marker is a record total of -1: without it the client opens an empty crime list the moment
        // the gathering phase starts.
        defendant.SendPacket(new SCCrimeRecordsPacket(trial.Id, PlayerTrialType, NoCaseFileYet, 0));

        if (_courtTrial[court] is null)
        {
            OpenCourt(trial);
            return trial;
        }

        UpdateQueueOrders(court);
        SendQueueStatus(trial);
        Logger.Info($"Trial {trial.Id}: {defendant.Name} waits for court {court} " +
                    $"(queue {trial.QueueOrder}, crime {trial.CrimePoint})");
        defendant.SendMessage(ChatType.System,
            $"The courtroom is busy - you are number {trial.QueueOrder} in its queue.");
        return trial;
    }

    /// <summary>Starts hearing a case: the court opens the file and calls for a bench.</summary>
    private void OpenCourt(Trial trial)
    {
        _courtTrial[trial.Court] = trial.Id;
        UpdateQueueOrders(trial.Court);

        // The court opens the case file, and the bench is summoned: the defendant waits at the court
        // (his own wait dialog carries the queue and the early-guilty plea) while invites go out.
        SetState(trial, TrialState.WaitingCrimeRecord);
        SetState(trial, TrialState.WaitingJury, TrialTimingRules.JuryGatherSeconds);
        SummonStandbyJurors(trial);

        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        Logger.Info($"Trial {trial.Id}: {trial.DefendantName} stands trial at court {trial.Court} " +
                    $"(crime {trial.CrimePoint}) - " +
                    $"the bench has {TrialTimingRules.JuryGatherSeconds}s to assemble");
        defendant?.SendMessage(ChatType.System, "Your trial has begun - jurors are being gathered.");
        ArmPhaseClock(trial, TrialTimingRules.JuryGatherSeconds, () => BeginTestimony(trial));
    }

    /// <summary>
    /// Invites standby jurors in waiting-number order, skipping anyone already invited, seated or
    /// serving elsewhere, and stops once every bench seat is spoken for.
    /// </summary>
    private void SummonStandbyJurors(Trial trial)
    {
        foreach (var characterId in _standby[trial.Court].ToArray())
        {
            if (trial.Jurors.Count + trial.Invited.Count >= RequiredJurors)
                break;

            var character = WorldManager.Instance.GetCharacterById(characterId);
            if (character is not { IsOnline: true })
            {
                // A standby number only means something while its holder can answer.
                _standby[trial.Court].Remove(characterId);
                continue;
            }

            if (trial.Jurors.Any(j => j.CharacterId == characterId) || trial.Invited.Contains(characterId))
                continue;

            Invite(trial, character);
        }

        SendWaitStatus(trial);
    }

    private void Invite(Trial trial, Character character)
    {
        trial.Invited.Add(character.Id);
        character.SendPacket(new SCInviteJuryPacket(trial.DefendantName, trial.Id));
        Logger.Info($"Trial {trial.Id}: {character.Name} invited to serve as a juror " +
                    $"({trial.Invited.Count} invited, {_standby[trial.Court].Count} on standby)");
    }

    public Trial GetTrial(ulong trialId) => _trials.GetValueOrDefault(trialId);

    /// <summary>Every case the court is holding, open or queued - what the trial test hook reports.</summary>
    public IReadOnlyCollection<Trial> LiveTrials => _trials.Values;

    /// <summary>The trial this character is on - as its defendant or as one of its jurors.</summary>
    public Trial GetLiveTrialOf(uint characterId)
    {
        if (_jurorTrial.TryGetValue(characterId, out var jurorTrialId) &&
            _trials.TryGetValue(jurorTrialId, out var jurorTrial))
            return jurorTrial;

        return _trials.Values.FirstOrDefault(t => t.DefendantId == characterId);
    }

    /// <summary>Sends court chat only for the defendant and jurors seated in the case currently being heard.</summary>
    public int SendChatMessage(Character origin, string message, int ability = 0, byte languageType = 0)
    {
        var trial = origin == null ? null : GetLiveTrialOf(origin.Id);
        var isCurrent = trial != null && _courtTrial[trial.Court] == trial.Id;
        if (!SocialChatAuthorization.CanUseTrialChat(trial, origin, isCurrent))
            return 0;

        var sent = 0;
        foreach (var characterId in trial.ParticipantIds.Distinct())
        {
            var recipient = WorldManager.Instance.GetCharacterById(characterId);
            if (recipient is not { IsOnline: true } ||
                !SocialChatAuthorization.CanUseTrialChat(trial, recipient, isCurrent))
                continue;

            recipient.SendPacket(new SCChatMessagePacket(ChatType.Judge, origin, message, ability, languageType));
            sent++;
        }

        return sent;
    }

    /// <summary>Volunteering as a juror hands out the next waiting number, as the client's court UI asks.</summary>
    public void OnWaitingNumberRequest(Character character)
    {
        if (character == null)
            return;

        if (GetLiveTrialOf(character.Id) != null)
        {
            Logger.Warn($"Trial: {character.Name} is already in a trial - no standby number");
            return;
        }

        var court = CourtOf(character);

        // The standby number is your place in your own court's queue: the same while you hold it,
        // whatever the client re-asks. Only a summons (or leaving) moves it.
        if (!_standby[court].Contains(character.Id))
            _standby[court].Add(character.Id);

        var waitingNumber = _standby[court].IndexOf(character.Id) + 1;
        character.SendPacket(new SCJuryWaitingNumberPacket(waitingNumber));
        Logger.Info($"Trial: {character.Name} holds jury waiting number {waitingNumber} of " +
                    $"{_standby[court].Count} at court {court}");

        // A trial that is still gathering its bench takes whoever signs up after it opened too.
        foreach (var trial in _trials.Values.Where(t => t.State == TrialState.WaitingJury).ToArray())
        {
            if (trial.Court != court || trial.Jurors.Count + trial.Invited.Count >= RequiredJurors)
                continue;
            if (trial.Jurors.Any(j => j.CharacterId == character.Id) || trial.Invited.Contains(character.Id))
                continue;

            Invite(trial, character);
            SendWaitStatus(trial);
        }
    }

    /// <summary>
    /// Seats an accepted juror. The court tells the client which chair is theirs with a summons and the
    /// client answers it by itself, so this runs from that answer - and the chair it names is the one
    /// the client will report back with every vote and every end-of-testimony.
    /// </summary>
    public bool SeatJuror(Character juror, ulong trialId, int juryNumber)
    {
        var trial = GetTrial(trialId);
        if (trial == null || juror == null)
            return false;

        // Seating is what hands over the case file and the vote, so it is bound to the gathering phase
        // even when a summons is still in flight: the promise below is only good while it lasts.
        if (!TrialJuryCallRules.IsCallLive(trial.State))
        {
            Logger.Warn($"Trial {trialId}: {juror.Name} answered a summons in phase {trial.State} - ignored");
            trial.Summoned.Remove(juror.Id);
            return false;
        }

        if (trial.FindJuror(juror.Id) != null)
            return false;

        if (trial.ResolveBench(juryNumber) is not { } bench)
        {
            Logger.Warn($"Trial {trialId}: chair {juryNumber} is not part of court {trial.Court}");
            return false;
        }

        // The court seats the chair it summoned this juror to, and only that one. A response naming any
        // other chair - or arriving from a character the court never summoned - is a stale or forged
        // packet: seating it would hand the case file and a vote to someone the bench never picked.
        // The promise itself is the check, so a chair still in flight stays spoken for.
        if (!trial.Summoned.TryGetValue(juror.Id, out var promisedChair) || promisedChair != juryNumber)
        {
            Logger.Warn($"Trial {trialId}: {juror.Name} answered a summons for chair {juryNumber} that was " +
                        "never promised - ignored");
            return false;
        }

        if (GetLiveTrialOf(juror.Id) is { } other && other.Id != trial.Id)
        {
            Logger.Warn($"Trial {trialId}: {juror.Name} is already serving on trial {other.Id}");
            return false;
        }

        var seat = new TrialJuror
        {
            CharacterId = juror.Id,
            Court = trial.Court,
            Seat = juryNumber,
            IsWest = bench.IsWest
        };
        lock (trial.Jurors)
            trial.Jurors.Add(seat);
        _jurorTrial[juror.Id] = trial.Id;
        _standby[trial.Court].Remove(juror.Id);
        trial.Invited.Remove(juror.Id);
        trial.Summoned.Remove(juror.Id);

        juror.SendPacket(new SCJuryBeSeatedPacket(bench.IsWest, trial.Id, trial.Court, juryNumber));

        // A juror serves from the bench: move them to the chair this seat names and sit them down.
        // The court's own zone is where the defendant is standing. A juror who is already there moves
        // seamlessly; a juror elsewhere has to load the courtroom zone exactly like a portal does -
        // without that load the destination's interior never streams and the courtroom looks empty.
        var defendantCharacter = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        var courtZoneId = defendantCharacter?.Transform.ZoneId ?? juror.Transform.ZoneId;
        var courtInstanceId = defendantCharacter?.Transform.InstanceId ?? juror.Transform.InstanceId;

        if (courtZoneId != juror.Transform.ZoneId || courtInstanceId != juror.Transform.InstanceId)
        {
            // Remember where they stood so the release can put them back once the trial is over.
            seat.ReturnPoint = new TrialReturnPoint(juror.Transform.ZoneId, juror.Transform.InstanceId,
                juror.Transform.World.Position.X, juror.Transform.World.Position.Y,
                juror.Transform.World.Position.Z, juror.Transform.World.Rotation.Z);
        }

        // The courtroom is open world. A zone hop (or a rare instance leave) goes through the
        // shared landing so the bench streams; a same-cell seat still restreams the room.
        var courtWorld = defendantCharacter?.ParentWorld ?? juror.ParentWorld;
        SkillTeleportLanding.TryApplyToWorld(
            juror, courtWorld, courtZoneId,
            bench.Position.X, bench.Position.Y, bench.Position.Z, 0f, TeleportReason.Jury);

        juror.Buffs.AddBuff(ArrestRules.SeatedJurorBuff, juror); // 배심원(앉기 버프) - the seated pose
        // The juror's own buff from the shipped data (3621 배심원) marks them as serving.
        juror.Buffs.AddBuff((uint)BuffConstants.Juror, juror);
        SendWaitStatus(trial);

        // The juror studies the case while the bench assembles: the sheet reaches them the moment
        // they take the seat, not when the trial opens.
        SendCrimeSheetTo(trial, juror);

        Logger.Info($"Trial {trial.Id}: {juror.Name} seated at court {trial.Court} chair {juryNumber} " +
                    $"({trial.Jurors.Count}/{RequiredJurors})");

        // The bench changed, so the court is told the current phase again with the new count: the
        // defendant's early-guilty plea is only offered while nobody is seated.
        RefreshState(trial);

        // A full bench has nothing left to wait for.
        if (trial.Jurors.Count >= RequiredJurors && trial.State == TrialState.WaitingJury)
            BeginTestimony(trial);

        return true;
    }

    public void OnSummoned(Character character, ulong trialId, int court, int jury)
    {
        var trial = GetTrial(trialId);
        if (trial == null || character == null)
            return;

        // The client answers a summons with the chair it was given; the court only hears cases of its
        // own courtroom, so a chair from anywhere else is refused.
        if (court != trial.Court)
        {
            Logger.Warn($"Trial {trialId}: {character.Name} answered a summons for court {court}, " +
                        $"but this case is heard at court {trial.Court}");
            return;
        }

        if (!SeatJuror(character, trialId, jury))
            Logger.Warn($"Trial {trialId}: {character.Name} could not be seated from a summons");
    }

    public void OnReplyInvite(Character character, bool accept, ulong trialId)
    {
        if (character == null)
            return;

        var trial = GetTrial(trialId);

        // The case can close while its invite dialog is still on screen. Nothing was refused and there
        // is no phase left to name, so a reply for a case that is gone is dropped, not read as a decline.
        if (trial == null)
        {
            Logger.Debug($"Trial {trialId}: {character.Name} answered a jury invite for a case that is gone - ignored");
            return;
        }

        if (!accept)
        {
            // A skipped invite gives up the standby number; the next in the queue is asked instead,
            // so the bench can still fill while the gathering window is open.
            _standby[trial.Court].Remove(character.Id);
            trial.Invited.Remove(character.Id);

            if (trial.State == TrialState.WaitingJury)
            {
                Logger.Info($"Trial {trialId}: {character.Name} skipped the jury invite");
                SummonStandbyJurors(trial);
            }
            else
            {
                // The invite dialog stays on screen until the client answers it, so a bench that is
                // already seated still gets a decline when the trial ends or the juror is released.
                // Nothing was refused - say so instead of reporting a decline that never happened.
                Logger.Info($"Trial {trialId}: {character.Name} declined an invite for a bench that is " +
                            $"no longer gathering (phase {trial.State})");
            }

            return;
        }

        // A juror may only join while the bench is still gathering. The case file is sent on seating,
        // so a late accept would hand the vote to someone who never read it with the bench.
        if (!TrialJuryCallRules.IsCallLive(trial.State))
        {
            Logger.Warn($"Trial {trialId}: {character.Name} accepted a jury invite in phase {trial.State} - ignored");
            trial.Invited.Remove(character.Id);
            character.SendErrorMessage(ErrorMessageType.TrialsCannotJoinAfterStart);
            return;
        }

        // Only a character the court actually invited may take a seat. The client sends the trial id, so
        // without this any player could accept an invitation that was never sent and vote on the case.
        if (!trial.Invited.Contains(character.Id))
        {
            Logger.Warn($"Trial {trialId}: {character.Name} accepted an invite they do not hold - ignored");
            return;
        }

        if (trial.NextFreeSeat() is not { } chair)
        {
            Logger.Warn($"Trial {trialId}: no free chair for {character.Name}");
            character.SendErrorMessage(ErrorMessageType.TrialsJuryFull);
            return;
        }

        // The chair is promised first and taken when the client answers the summons: the summon is
        // also what tells the client it is a juror, which is what arms its verdict window.
        trial.Summoned[character.Id] = chair;
        character.SendPacket(new SCSummonJuryPacket(trial.Id, trial.Court, chair));
        Logger.Info($"Trial {trialId}: {character.Name} accepted the invite - summoned to chair {chair}");
    }

    /// <summary>
    /// A juror is done reading the crime record. Once every seated juror has said so the bench has the
    /// defendant speak; the phase clock backstops a bench that never answers.
    /// </summary>
    public void OnEndTestimony(Character character, ulong trialId, int jury)
    {
        var trial = GetTrial(trialId);
        if (trial == null || character == null)
            return;

        var seat = trial.FindJuror(character.Id);
        if (seat == null)
        {
            Logger.Warn($"Trial {trialId}: {character.Name} is not on this bench - record review ignored");
            return;
        }

        if (seat.ReadRecord)
            return;

        seat.ReadRecord = true;
        Logger.Info($"Trial {trialId}: {character.Name} finished reading the record " +
                    $"({trial.ReadRecordCount}/{trial.Jurors.Count})");

        Broadcast(trial, new SCChangeJuryOKCountPacket(trial.ReadRecordCount, trial.Jurors.Count));

        if (trial.State == TrialState.Testimony && trial.ReadRecordCount >= trial.Jurors.Count)
            BeginFinalStatement(trial);
    }

    /// <summary>The defendant cuts their final statement short so the bench can vote.</summary>
    public void OnSkipFinalStatement(Character character, ulong trialId)
    {
        var trial = GetTrial(trialId);
        if (trial == null || character == null || character.Id != trial.DefendantId)
            return;

        if (trial.State != TrialState.FinalStatement)
            return;

        Logger.Info($"Trial {trialId}: {character.Name} skipped the final statement");
        BeginVote(trial);
    }

    /// <summary>
    /// A juror's vote: the client sends the row it picked, where the first row is not guilty and the
    /// next five are the guilty tiers. The verdict lands once every seated juror has voted.
    /// </summary>
    public void OnVerdict(Character character, ulong trialId, int jury, byte choice)
    {
        var trial = GetTrial(trialId);
        if (trial == null || character == null)
            return;

        var seat = trial.FindJuror(character.Id);
        if (seat == null)
        {
            Logger.Warn($"Trial {trialId}: vote from {character.Name} who is not on this bench - ignored");
            return;
        }

        if (seat.Voted)
            return;

        if (!TrialVerdictRules.IsValidChoice(choice))
        {
            Logger.Warn($"Trial {trialId}: {character.Name} sent sentence choice {choice}, " +
                        "which is not a row of the court's verdict window - ignored");
            return;
        }

        // The vote only counts in the phase that exists to count it: the bench reads the record and
        // hears the final statement first, so an earlier vote would let the jury close the case before
        // the defendant has spoken.
        if (!TrialVerdictRules.CanVote(trial.State))
        {
            Logger.Warn($"Trial {trialId}: vote from {character.Name} arrived in phase {trial.State} - ignored");
            return;
        }

        // The vote is the juror's own: the tally is read off the seated bench every time, so a juror who
        // is released later cannot keep deciding a case they are no longer on.
        seat.Choice = choice;

        Logger.Info($"Trial {trialId}: {character.Name} voted " +
                    $"{(TrialVerdictRules.IsGuiltyChoice(choice) ? $"guilty tier {TrialVerdictRules.GuiltyTier(choice)}" : "not guilty")} " +
                    $"({trial.GuiltyVotes}/{trial.Jurors.Count} guilty)");

        Broadcast(trial, new SCChangeJuryVerdictCountPacket(trial.VotedCount, trial.Jurors.Count));
        SendTrialInfo(trial);

        var verdict = TrialVerdictRules.Tally(trial.GuiltyVotes, trial.NotGuiltyVotes, trial.Jurors.Count);
        if (verdict != TrialVerdict.Pending)
            Conclude(trial, verdict, TrialVerdictRules.RulingChoice(verdict, trial.HighestGuiltyChoice));

        // No running tally is broadcast here on purpose: the ruling packet is what shows the court's
        // result window, and the client closes the juror's verdict window the moment it arrives - a
        // mid-vote tally would take the vote away from every juror who had not answered yet.
    }

    /// <summary>
    /// The defendant admits guilt from the wait window. The bench never sits, and the court applies
    /// the default sentence.
    /// </summary>
    public void OnCancel(Character character, ulong trialId)
    {
        var trial = GetTrial(trialId);
        if (trial == null || character == null || character.Id != trial.DefendantId)
            return;

        // Giving up is offered in the wait window, before the bench is seated. A cancel that arrives once
        // the case is being heard - or while the jury is voting - cannot replace that with a plea.
        if (!TrialVerdictRules.CanPleadGuilty(trial.State))
        {
            Logger.Warn($"Trial {trialId}: {character.Name} tried to give up in phase {trial.State} - ignored");
            return;
        }

        Logger.Info($"Trial {trialId}: {character.Name} admitted guilt - the default sentence applies");
        SetState(trial, TrialState.GuiltyByUser);
        Conclude(trial, TrialVerdict.Guilty, TrialSentenceRules.BaseSentenceChoice());
    }

    /// <summary>
    /// An onlooker takes a seat in the gallery. The court they are watching from is the one whose
    /// case is nearest them, so a visitor to the other continent's court watches that court's trial
    /// rather than their own faction's.
    /// </summary>
    public void JoinAudience(Character character, uint trialType)
    {
        if (character == null)
            return;

        // The client sends the trial type it was told about, and only the player trial has a courtroom
        // to sit in - anything else belongs to the bot-report windows.
        if (trialType != PlayerTrialType)
        {
            Logger.Warn($"Trial: {character.Name} asked to watch trial type {trialType} - not a player trial");
            return;
        }

        var trial = ClosestLiveTrial(character);

        if (trial == null)
        {
            character.SendErrorMessage(ErrorMessageType.TrialsAlreadyClosed);
            return;
        }

        if (!TrialAudienceRules.CanWatch(trial.State))
        {
            character.SendErrorMessage(ErrorMessageType.TrialsCannotJoinAfterStart);
            return;
        }

        if (!TrialAudienceRules.CanJoinGallery(
                trial.DefendantId == character.Id, trial.FindJuror(character.Id) != null))
        {
            return;
        }

        // The gallery is a room in the courthouse, so the onlooker has to be in it - in the world the
        // courthouse stands in, not a copy of its coordinates inside an instance, where the same X and
        // Y are a different place entirely. Without this check the packet alone would hand the
        // defendant's crime file to anyone anywhere in the world.
        var distanceToCourt = DistanceToCourt(character, trial);
        if (character.Transform.InstanceId != WorldManager.DefaultInstanceId ||
            !TrialAudienceRules.InGalleryRange(distanceToCourt))
        {
            Logger.Info($"Trial {trial.Id}: {character.Name} asked to watch from outside the courtroom " +
                        $"(instance {character.Transform.InstanceId}, {distanceToCourt:F0} m away) - refused");
            character.SendErrorMessage(ErrorMessageType.TrialsCannotJoinAfterStart);
            return;
        }

        _audienceTrial[character.Id] = trial.Id;

        // Register first: every send below broadcasts to the gallery, so the new member is on the
        // list by the time the file and the phase go out.
        SendCrimeSheetTo(trial, character);
        SendTrialInfo(trial);
        RefreshState(trial);
        Broadcast(trial, new SCTrialAudienceJoinedPacket(trial.Id, character.ObjId, character.Name));
        Logger.Info($"Trial {trial.Id}: {character.Name} joined the audience " +
                    $"(phase {trial.State}, sheet sent)");
    }

    /// <summary>
    /// The live case being heard nearest the character - the courtrooms stand where
    /// <see cref="ArrestRules.CourtPositionFor"/> puts them, so distance is what says which court the
    /// character is standing in.
    /// </summary>
    private Trial ClosestLiveTrial(Character character) =>
        _trials.Values
            .Where(t => t.State is not (TrialState.PostSentence or TrialState.Free))
            .OrderBy(t => DistanceToCourt(character, t))
            .FirstOrDefault();

    /// <summary>
    /// How far a character stands from the courthouse hearing a case - the distance the gallery
    /// admission is judged on, and what orders the courts when more than one is hearing a case.
    /// </summary>
    private static double DistanceToCourt(Character character, Trial trial)
    {
        var (x, y, _) = ArrestRules.CourtPositionFor(
            trial.Court == TrialSeatRules.CourtForNation(true)
                ? AAEmu.Game.Models.StaticValues.FactionsEnum.NuiaAlliance
                : AAEmu.Game.Models.StaticValues.FactionsEnum.HaranyaAlliance);
        var position = character.Transform.World.Position;
        var dx = position.X - x;
        var dy = position.Y - y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public void LeaveAudience(Character character)
    {
        if (character == null || !_audienceTrial.Remove(character.Id, out var trialId))
            return;

        var trial = GetTrial(trialId);
        if (trial == null)
            return;

        Broadcast(trial, new SCTrialAudienceLeftPacket(character.ObjId, character.Name));
        Logger.Info($"Trial {trialId}: {character.Name} left the audience");
    }

    /// <summary>
    /// Somebody left the world. A defendant who disconnects closes their own case; a juror who leaves
    /// gives up their chair so the queue can fill it again.
    /// </summary>
    public void OnCharacterLogout(Character character)
    {
        if (character == null)
            return;

        _audienceTrial.Remove(character.Id);

        foreach (var list in _standby)
            list.Remove(character.Id);

        // An invitation and a promised chair are one-shot promises to a live session. A character who
        // disconnects between the invite and the summon reply is neither a defendant nor a seated juror,
        // so GetLiveTrialOf cannot see them - without this their reservation keeps consuming a bench seat
        // and the court would never ask the next standby juror.
        foreach (var gathering in _trials.Values.Where(t => t.State == TrialState.WaitingJury).ToArray())
        {
            var invited = gathering.Invited.Remove(character.Id);
            var promised = gathering.Summoned.Remove(character.Id);
            if (!invited && !promised)
                continue;

            Logger.Info($"Trial {gathering.Id}: {character.Name} left the world - the jury invite is withdrawn");
            SummonStandbyJurors(gathering);
        }

        var trial = GetLiveTrialOf(character.Id);
        if (trial == null)
            return;

        if (trial.DefendantId == character.Id)
        {
            Logger.Info($"Trial {trial.Id}: the defendant {character.Name} left the world - " +
                        "the case is closed and the record stands");
            CloseTrial(trial, new SCTrialCancledPacket(trial.Id));
            return;
        }

        var juror = trial.FindJuror(character.Id);
        if (juror == null)
            return;

        ReleaseJuror(trial, juror, sendHome: false);
        Logger.Info($"Trial {trial.Id}: the juror {character.Name} left the world " +
                    $"({trial.Jurors.Count}/{RequiredJurors} left)");

        if (trial.Jurors.Count == 0)
        {
            // A bench that walked out cannot hear the case; the defendant's record stands.
            Logger.Info($"Trial {trial.Id}: every juror left - the court applies the default sentence");
            SetState(trial, TrialState.GuiltyBySystem);
            Conclude(trial, TrialVerdict.Guilty, TrialSentenceRules.BaseSentenceChoice());
            return;
        }

        if (trial.State == TrialState.WaitingJury)
            SummonStandbyJurors(trial);
        else if (trial.State == TrialState.Testimony && trial.ReadRecordCount >= trial.Jurors.Count)
            BeginFinalStatement(trial);
        else if (trial.State == TrialState.Sentence && trial.VotedCount >= trial.Jurors.Count)
            ConcludeOnTheVotes(trial);
        else
            AnnouncePhase(trial);
    }

    /// <summary>
    /// Closes the case on the votes the bench still holds. Used when the last vote of a sitting bench
    /// is already in and a juror leaves: the ruling is the tally's own verdict, so a not-guilty
    /// majority is not read out as a guilty row with a zero sentence.
    /// </summary>
    private void ConcludeOnTheVotes(Trial trial)
    {
        var verdict = TrialVerdictRules.Tally(trial.GuiltyVotes, trial.NotGuiltyVotes, trial.Jurors.Count);
        if (verdict == TrialVerdict.Pending)
        {
            // Every remaining seat voted, so this is unreachable - but a pending tally must not be
            // read out as either verdict, and the court still owes its clients the current phase.
            AnnouncePhase(trial);
            return;
        }

        Conclude(trial, verdict, TrialVerdictRules.RulingChoice(verdict, trial.HighestGuiltyChoice));
    }

    /// <summary>Tells every participant which phase the court is in and how long it has left.</summary>
    private void AnnouncePhase(Trial trial) =>
        Broadcast(trial, new SCChangeTrialStatePacket(trial.Id, (byte)trial.State, trial.Jurors.Count,
            TrialTimingRules.ToClientMilliseconds(RemainingSeconds(trial))));

    // ---------------------------------------------------------------------------------------------
    // phases
    // ---------------------------------------------------------------------------------------------

    /// <summary>The bench has gathered: the case file is opened and the bench reads it.</summary>
    private void BeginTestimony(Trial trial)
    {
        if (trial.State is not (TrialState.WaitingJury or TrialState.WaitingCrimeRecord))
            return;

        if (trial.Jurors.Count == 0)
        {
            Logger.Info($"Trial {trial.Id}: nobody joined the bench - the defendant's record stands");
            SetState(trial, TrialState.GuiltyBySystem);
            Conclude(trial, TrialVerdict.Guilty, TrialSentenceRules.BaseSentenceChoice());
            return;
        }

        SetState(trial, TrialState.WaitingCrimeRecord);

        // The courtroom case opens as the bench convenes: the tally, then the crime sheet both the
        // defendant and the jurors read the case from.
        SendTrialInfo(trial);
        SendCrimeSheet(trial);
        SetState(trial, TrialState.Testimony, TrialTimingRules.TestimonySeconds);

        Logger.Info($"Trial {trial.Id}: the bench of {trial.Jurors.Count} reads the record " +
                    $"({TrialTimingRules.TestimonySeconds}s)");

        ArmPhaseClock(trial, TrialTimingRules.TestimonySeconds, () => BeginFinalStatement(trial));
    }

    /// <summary>The defendant's final statement, then the bench votes.</summary>
    private void BeginFinalStatement(Trial trial)
    {
        if (trial.State is not (TrialState.Testimony or TrialState.FinalStatement))
            return;

        SetState(trial, TrialState.FinalStatement, TrialTimingRules.FinalStatementSeconds);

        Logger.Info($"Trial {trial.Id}: the defendant's final statement " +
                    $"({TrialTimingRules.FinalStatementSeconds}s)");
        WorldManager.Instance.GetCharacterById(trial.DefendantId)?
            .SendMessage(ChatType.System,
                $"You have {TrialTimingRules.FinalStatementSeconds} seconds for your final statement.");

        ArmPhaseClock(trial, TrialTimingRules.FinalStatementSeconds, () => BeginVote(trial));
    }

    /// <summary>The bench votes. The client arms its verdict window on this phase.</summary>
    private void BeginVote(Trial trial)
    {
        if (trial.State is not (TrialState.FinalStatement or TrialState.Sentence or TrialState.Testimony))
            return;

        SetState(trial, TrialState.Sentence, TrialTimingRules.VoteSeconds);

        Logger.Info($"Trial {trial.Id}: the bench decides ({TrialTimingRules.VoteSeconds}s)");
        WorldManager.Instance.GetCharacterById(trial.DefendantId)?
            .SendMessage(ChatType.System, "The jury is deciding your sentence.");

        ArmPhaseClock(trial, TrialTimingRules.VoteSeconds, () => ConcludeOnSilentBench(trial));
    }

    /// <summary>
    /// The vote window ran out. A bench that answered decides; one that never answered lets the
    /// defendant's record stand rather than hanging the case.
    /// </summary>
    private void ConcludeOnSilentBench(Trial trial)
    {
        var verdict = TrialVerdictRules.Tally(trial.GuiltyVotes, trial.NotGuiltyVotes, trial.Jurors.Count);
        if (verdict != TrialVerdict.Pending)
        {
            Conclude(trial, verdict, TrialVerdictRules.RulingChoice(verdict, trial.HighestGuiltyChoice));
            return;
        }

        Logger.Info($"Trial {trial.Id}: the vote closed with {trial.GuiltyVotes} guilty / " +
                    $"{trial.NotGuiltyVotes} not guilty of {trial.Jurors.Count} chairs - " +
                    "the record stands");
        SetState(trial, TrialState.GuiltyBySystem);
        Conclude(trial, TrialVerdict.Guilty, TrialSentenceRules.BaseSentenceChoice());
    }

    /// <summary>
    /// Reads the ruling out, applies it and closes the court. The sentence itself goes through the
    /// same jail path the arrest flow uses.
    /// </summary>
    private void Conclude(Trial trial, TrialVerdict verdict, byte rulingChoice)
    {
        if (trial.State is not (TrialState.GuiltyBySystem or TrialState.GuiltyByUser))
            SetState(trial, TrialState.Sentence);

        // A guilty row carries a share of the case's base sentence, so the ruling reads out the same
        // number the bench picked: the court cannot serve a length the shipped prisoner buff does not
        // have, and that difference is what the log line below records.
        var baseSentenceMs = SentenceMilliseconds(trial);
        var sentenceMs = verdict == TrialVerdict.Guilty
            ? TrialSentenceRules.SentenceMilliseconds(baseSentenceMs, rulingChoice)
            : 0u;

        Broadcast(trial, new SCRulingStatusPacket(trial.Jurors.Count, trial.Jurors.Count, rulingChoice, sentenceMs));

        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        if (verdict == TrialVerdict.Guilty)
        {
            var ruledMinutes = (int)(sentenceMs / 60000u);
            Logger.Info($"Trial {trial.Id}: guilty - the bench read out {ruledMinutes} minutes " +
                        $"({TrialSentenceRules.RatioPercent(rulingChoice)}% of the {trial.SentenceMinutes}-minute base); " +
                        $"the shipped sentence is {trial.SentenceMinutes} minutes");
            if (defendant is { IsOnline: true })
            {
                // The sentence lands first: a jail that is not configured must leave the record on the
                // books, because nothing was served and the crime was not paid. Only the crimes this case
                // was actually about are closed with it.
                if (JusticeManager.Instance.ServeSentence(defendant, (uint)ruledMinutes))
                    CrimeManager.Instance.ExpungeCrimesOfPlayer(defendant.Id, trial.TriedCrimeIds);
                else
                    Logger.Warn($"Trial {trial.Id}: the sentence did not land - the record stays on the books");
            }
            else
            {
                Logger.Warn($"Trial {trial.Id}: {trial.DefendantName} is not online - the sentence is not " +
                            "served and the record stays on the books");
            }
        }
        else
        {
            Logger.Info($"Trial {trial.Id}: not guilty - the defendant walks free");
            if (defendant is { IsOnline: true })
            {
                defendant.Buffs.RemoveBuff(ArrestRules.ForcedMoveToCourtBuff);
                defendant.SendMessage(ChatType.System, "The jury found you not guilty.");
            }
        }

        SetState(trial, TrialState.PostSentence);
        Broadcast(trial, new SCRulingClosedPacket());
        CloseTrial(trial, null);
    }

    /// <summary>
    /// Takes a trial down: the bench is released, the audience is let go and the case file is closed.
    /// The courtroom is then free, so the next case waiting for it opens.
    /// </summary>
    private void CloseTrial(Trial trial, SCTrialCancledPacket cancelPacket)
    {
        if (cancelPacket != null)
            Broadcast(trial, cancelPacket);

        foreach (var juror in trial.Jurors.ToArray())
            ReleaseJuror(trial, juror, sendHome: true);

        // A case that ends without a ruling still ends the courthouse state: the defendant is no longer
        // being heard, and the state outlives the session by ten hours if nobody takes it off.
        WorldManager.Instance.GetCharacterById(trial.DefendantId)?
            .Buffs.RemoveBuff(ArrestRules.ForcedMoveToCourtBuff);

        foreach (var (characterId, trialId) in _audienceTrial.ToArray())
        {
            if (trialId == trial.Id)
                _audienceTrial.Remove(characterId);
        }

        _trials.Remove(trial.Id);

        if (_courtTrial[trial.Court] != trial.Id)
        {
            // A queued case left without being heard: the queue behind it moves up.
            UpdateQueueOrders(trial.Court);
            return;
        }

        _courtTrial[trial.Court] = null;

        // Whoever has been waiting longest for this courtroom is heard next.
        var next = _trials.Values
            .Where(t => t.Court == trial.Court)
            .OrderBy(t => t.Id)
            .FirstOrDefault();

        if (next == null)
            return;

        Logger.Info($"Trial {next.Id}: court {trial.Court} is free - {next.DefendantName} is heard next");
        OpenCourt(next);
    }

    /// <summary>
    /// Numbers the cases waiting for a courtroom in the order they will be heard, and tells their
    /// defendants: the wait dialog is where a defendant reads their place in the queue.
    /// </summary>
    private void UpdateQueueOrders(int court)
    {
        var queue = _trials.Values
            .Where(t => t.Court == court)
            .OrderBy(t => t.Id)
            .ToArray();

        for (var i = 0; i < queue.Length; i++)
        {
            queue[i].QueueOrder = (uint)(i + 1);

            if (queue[i].Id != _courtTrial[court])
                SendQueueStatus(queue[i]);
        }
    }

    /// <summary>The waiting defendant's own dialog: their place in the court queue and the sentence.</summary>
    private void SendQueueStatus(Trial trial)
    {
        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        defendant?.SendPacket(new SCTrialWaitStatusPacket(trial.QueueOrder, SentenceMilliseconds(trial)));
    }

    /// <summary>
    /// Takes a juror off the bench: the serving buffs come off - both are permanent in the shipped
    /// data, so nothing else would ever clear them - and a juror the court pulled in from elsewhere is
    /// put back where they stood.
    /// </summary>
    private void ReleaseJuror(Trial trial, TrialJuror juror, bool sendHome)
    {
        _jurorTrial.Remove(juror.CharacterId);
        lock (trial.Jurors)
            trial.Jurors.Remove(juror);
        trial.Invited.Remove(juror.CharacterId);
        trial.Summoned.Remove(juror.CharacterId);

        var character = WorldManager.Instance.GetCharacterById(juror.CharacterId);
        if (character is not { IsOnline: true })
            return;

        character.Buffs.RemoveBuff(ArrestRules.SeatedJurorBuff);
        character.Buffs.RemoveBuff((uint)BuffConstants.Juror);

        if (!sendHome || juror.ReturnPoint is not { } home)
            return;

        SkillTeleportLanding.ApplyWorld(
            character, home.ZoneId, home.X, home.Y, home.Z, home.Yaw, TeleportReason.Etc);
    }

    // ---------------------------------------------------------------------------------------------
    // case file
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The case file the trial window shows: the defendant and the record rows he is tried for.
    /// Null while the defendant is offline - there is nothing to read the case from.
    /// </summary>
    private (SCCrimeDataPacket Sheet, SCCrimeRecordsPacket Records, int Rows)? BuildCrimeSheet(Trial trial)
    {
        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        if (defendant is not { IsOnline: true })
            return null;

        // Every reader reads the file the case was opened with - never the defendant's live list, which
        // would grow under a case that is already being heard.
        var crimes = CrimeManager.Instance.GetCrimesOfPlayer(defendant.Id)
            .Where(c => trial.IsTriedCrime(c.Id))
            .ToList();

        var rows = new List<CrimeRecordEntry>(crimes.Count);
        foreach (var crime in crimes)
        {
            rows.Add(new CrimeRecordEntry(
                crime.Id,
                0, // unnamed in the client's reader
                NameOf(crime.Victim),
                0, // unnamed in the client's reader
                NameOf(crime.Reporter),
                0, // faction the row falls back to - the client only uses it when the primary is unset
                0, // primary faction of the row; the crime itself carries no faction
                (byte)crime.CrimeKind,
                crime.DoodadTemplate, // the client resolves this id to the row's "what was stolen" name
                crime.Arg1,           // the report's own skill id, which the client resolves to a name
                crime.Position.X, crime.Position.Y, crime.Position.Z,
                crime.Msg,
                // The client renders this through the platform's local-time helper, which wants a
                // Windows file time - a unix timestamp comes out as a date in the year 1600.
                (ulong)DateTime.SpecifyKind(crime.ReportTime, DateTimeKind.Utc).ToFileTimeUtc()));
        }

        // The sheet's own sentence is the base the client's verdict window takes its five options
        // from, and that base is in milliseconds like every other clock the court sends.
        return (new SCCrimeDataPacket(PlayerTrialType, defendant.Name, (byte)defendant.Race, 0, trial.Id,
                    SentenceMilliseconds(trial), defendant.ObjId),
                new SCCrimeRecordsPacket(trial.Id, PlayerTrialType, (uint)rows.Count, rows),
                rows.Count);
    }

    /// <summary>The case file for the whole court: the defendant and every seated juror.</summary>
    private void SendCrimeSheet(Trial trial)
    {
        if (BuildCrimeSheet(trial) is not { } built)
            return;

        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        defendant?.SendPacket(built.Sheet);
        defendant?.SendPacket(built.Records);

        foreach (var juror in trial.Jurors)
        {
            var character = WorldManager.Instance.GetCharacterById(juror.CharacterId);
            if (character is not { IsOnline: true })
                continue;

            character.SendPacket(built.Sheet);
            character.SendPacket(built.Records);
        }

        Logger.Info($"Trial {trial.Id}: crime sheet sent ({built.Rows} record rows)");
    }

    /// <summary>The case file for one reader - a juror gets theirs the moment they take the seat.</summary>
    private void SendCrimeSheetTo(Trial trial, Character target)
    {
        if (BuildCrimeSheet(trial) is not { } built)
            return;

        target.SendPacket(built.Sheet);
        target.SendPacket(built.Records);
        Logger.Info($"Trial {trial.Id}: crime sheet sent to {target.Name} ({built.Rows} record rows)");
    }

    /// <summary>
    /// A name while its character is online. Offline characters have no name cache to resolve against
    /// yet, so the row shows an empty cell rather than a guessed name.
    /// </summary>
    private static string NameOf(uint characterId) =>
        WorldManager.Instance.GetCharacterById(characterId)?.Name ?? string.Empty;

    /// <summary>
    /// The tally the court UI reads. The two time slots are durations in seconds, not counters: the
    /// client labels the first one "total time imprisoned" and formats it as a date span. The court
    /// does not keep a running total of past sentences yet, so the case's own sentence is what it can
    /// honestly report.
    /// </summary>
    private void SendTrialInfo(Trial trial)
    {
        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        var info = new SCTrialInfoPacket(
            PlayerTrialType,
            trial.CrimePoint,
            0, // times this defendant has been brought in - not tracked yet
            0, // times this defendant has admitted guilt - not tracked yet
            0, // times this defendant has asked for a trial - not tracked yet
            trial.NotGuiltyVotes,
            trial.GuiltyVotes,
            (int)(trial.SentenceMinutes * 60u),
            0, // the client exposes this as "current time" and no window reads it
            defendant?.CrimeRecord ?? 0,
            0);
        Broadcast(trial, info);
    }

    /// <summary>Tells the court the phase it is already in again, with how much of it is left.</summary>
    private void RefreshState(Trial trial)
    {
        Broadcast(trial, new SCChangeTrialStatePacket(trial.Id, (byte)trial.State, trial.Jurors.Count,
            TrialTimingRules.ToClientMilliseconds(RemainingSeconds(trial))));
    }

    /// <summary>The case's sentence in the milliseconds every court clock is expressed in.</summary>
    private static uint SentenceMilliseconds(Trial trial) =>
        TrialTimingRules.ToClientMilliseconds(trial.SentenceMinutes * 60u);

    /// <summary>
    /// The court's wait status. Both wait dialogs are the defendant's own window, so they go to the
    /// defendant only; the standby jurors get the invite and their queue number instead.
    /// </summary>
    private void SendWaitStatus(Trial trial)
    {
        var defendant = WorldManager.Instance.GetCharacterById(trial.DefendantId);
        if (defendant is not { IsOnline: true })
            return;

        var sentenceMs = SentenceMilliseconds(trial);
        defendant.SendPacket(new SCJuryWaitStatusPacket(trial.Jurors.Count, RequiredJurors, sentenceMs));
        defendant.SendPacket(new SCTrialWaitStatusPacket(trial.QueueOrder, sentenceMs));
    }

    // ---------------------------------------------------------------------------------------------
    // phases plumbing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves the trial to a phase and tells every participant, with the seconds left on that phase
    /// so the client's own clock runs down with ours.
    /// </summary>
    private void SetState(Trial trial, TrialState state, int remainSeconds = 0)
    {
        // A jury call is only good while the bench is gathering. The clients keep the dialog on screen
        // until they answer it, so the calls are forgotten here the moment that phase ends; the accept
        // and seating paths refuse anything that still arrives afterwards.
        if (TrialJuryCallRules.ShouldDropPendingCalls(trial.State, state))
            DropPendingJuryCalls(trial);

        trial.State = state;
        trial.PhaseToken++;
        trial.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, remainSeconds));
        Broadcast(trial, new SCChangeTrialStatePacket(trial.Id, (byte)state, trial.Jurors.Count,
            TrialTimingRules.ToClientMilliseconds(remainSeconds)));
    }

    /// <summary>
    /// Forgets every unanswered invitation and every promised chair of a bench that stopped gathering.
    /// </summary>
    private void DropPendingJuryCalls(Trial trial)
    {
        if (trial.Invited.Count == 0 && trial.Summoned.Count == 0)
            return;

        Logger.Info($"Trial {trial.Id}: the bench stopped gathering - {trial.Invited.Count} invite(s) and " +
                    $"{trial.Summoned.Count} summons dropped");
        trial.Invited.Clear();
        trial.Summoned.Clear();
    }

    private static int RemainingSeconds(Trial trial) =>
        (int)Math.Max(0, (trial.PhaseEndsUtc - DateTime.UtcNow).TotalSeconds);

    /// <summary>
    /// Arms one phase's clock through the task manager, so the phase change happens on the same thread
    /// as every other game timer and a bad clock cannot swallow its own exception the way a
    /// fire-and-forget continuation did.
    /// </summary>
    private static void ArmPhaseClock(Trial trial, int seconds, Action onElapsed)
    {
        if (seconds <= 0)
            return;

        TaskManager.Instance.Schedule(
            new TrialPhaseClockTask(trial.Id, trial.PhaseToken, onElapsed),
            TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// The phase clock ran out. Only the phase that armed it may act: every phase change bumps the
    /// token, so a clock whose phase ended early (every juror answered, the defendant gave up) does
    /// nothing.
    /// </summary>
    public void CompletePhaseClock(ulong trialId, int phaseToken, Action onElapsed)
    {
        var trial = GetTrial(trialId);
        if (trial == null || trial.PhaseToken != phaseToken)
            return;

        onElapsed();
    }

    private void Broadcast(Trial trial, GamePacket packet)
    {
        foreach (var id in trial.ParticipantIds.Distinct())
        {
            var character = WorldManager.Instance.GetCharacterById(id);
            character?.SendPacket(packet);
        }

        foreach (var (characterId, trialId) in _audienceTrial.ToArray())
        {
            if (trialId != trial.Id)
                continue;
            WorldManager.Instance.GetCharacterById(characterId)?.SendPacket(packet);
        }
    }

    private static int CourtOf(Character character) =>
        TrialSeatRules.CourtForNation(character.Faction?.MotherId == AAEmu.Game.Models.StaticValues.FactionsEnum.NuiaAlliance);
}
