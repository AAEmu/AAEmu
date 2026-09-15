namespace AAEmu.Game.Models.Game.Justice;

/// <summary>One seated juror of a trial.</summary>
public class TrialJuror
{
    public uint CharacterId { get; init; }
    public int Court { get; init; }
    public int Seat { get; init; }
    public bool IsWest { get; init; }

    /// <summary>True once the juror has told the court they are done reading the crime record.</summary>
    public bool ReadRecord { get; set; }

    /// <summary>
    /// The juror's vote as the client's own sentence-choice constant, or 0 while they have not voted.
    /// The client's first row is "not guilty", so the raw byte cannot be read as a sentence.
    /// </summary>
    public byte Choice { get; set; }

    public bool Voted => Choice != 0;

    /// <summary>
    /// Where the juror stood before the court moved them onto the bench, so the release can put them
    /// back. Null for a juror who was already standing in the courtroom.
    /// </summary>
    public TrialReturnPoint? ReturnPoint { get; set; }
}

/// <summary>A place a released participant goes back to.</summary>
public readonly record struct TrialReturnPoint(uint ZoneId, uint InstanceId, float X, float Y, float Z, float Yaw);

/// <summary>
/// A live trial: the defendant, the jurors who were seated for it, their votes and the phase the
/// client is being told about through SCChangeTrialState.
/// </summary>
public class Trial
{
    public ulong Id { get; init; }
    public uint DefendantId { get; init; }

    /// <summary>The defendant's name as the jury invite shows it.</summary>
    public string DefendantName { get; init; } = string.Empty;

    /// <summary>Which courthouse hears the case - it is the defendant's own faction's court.</summary>
    public int Court { get; init; }

    /// <summary>The defendant's place in the court queue, as the wait dialog shows it.</summary>
    public uint QueueOrder { get; set; } = 1;

    public int CrimePoint { get; init; }
    public TrialState State { get; set; } = TrialState.Free;

    public List<TrialJuror> Jurors { get; } = [];
    public int GuiltyVotes { get; set; }
    public int NotGuiltyVotes { get; set; }

    /// <summary>The heaviest guilty row any juror chose - the sentence the ruling reads out.</summary>
    public byte HighestGuiltyChoice { get; set; }

    /// <summary>
    /// The sentence this case carries, in minutes. The shipped prisoner buff runs thirty minutes and
    /// there is one of them for a normal crime, so every guilty verdict serves that.
    /// </summary>
    public uint SentenceMinutes { get; init; } = ArrestRules.SentenceMinutes;

    /// <summary>The standby queue members already invited to this trial, in queue order.</summary>
    public List<uint> Invited { get; } = [];

    /// <summary>
    /// Chairs already promised to a juror. A juror is told which chair to take and answers with the
    /// summon reply, so between those two packets the chair is spoken for even though nobody sits in
    /// it yet - otherwise two jurors are handed the same chair.
    /// </summary>
    public Dictionary<uint, int> Summoned { get; } = [];

    /// <summary>
    /// The crime records this case is about, fixed once when the case opens. A crime reported after that
    /// is not part of this trial: it must not appear on a sheet a later reader gets, and a guilty verdict
    /// must not expunge it. A defendant with nothing on the books opens an empty file, and that empty
    /// file is still the case - it must not be filled in later.
    /// </summary>
    public HashSet<uint> TriedCrimeIds { get; } = [];

    /// <summary>True once the case file has been fixed. An empty file is still an opened file.</summary>
    public bool CaseFileOpened { get; private set; }

    /// <summary>
    /// Fixes the case file to the crimes the defendant is being tried for. Only the first call counts:
    /// every later reader (a juror seated late, an onlooker) reads this same case, and a crime reported
    /// while the bench is sitting joins neither the sheet nor the verdict.
    /// </summary>
    public void OpenCaseFile(IEnumerable<uint> crimeIds)
    {
        if (CaseFileOpened)
            return;

        CaseFileOpened = true;
        foreach (var id in crimeIds)
            TriedCrimeIds.Add(id);
    }

    /// <summary>True for a record this case was opened with.</summary>
    public bool IsTriedCrime(uint crimeId) => TriedCrimeIds.Contains(crimeId);

    /// <summary>Bumped on every phase change, so a timer armed for an older phase goes quiet.</summary>
    public int PhaseToken { get; set; }

    /// <summary>When the phase the client is watching runs out (gathering window, phase clock).</summary>
    public DateTime PhaseEndsUtc { get; set; }

    public IReadOnlyList<uint> ParticipantIds
    {
        get
        {
            var ids = new List<uint> { DefendantId };
            foreach (var juror in Jurors)
                ids.Add(juror.CharacterId);
            return ids;
        }
    }

    public TrialJuror FindJuror(uint characterId) =>
        Jurors.FirstOrDefault(j => j.CharacterId == characterId);

    /// <summary>True when the chair is already taken by another juror of this trial.</summary>
    public bool IsSeatTaken(int court, int juryNumber) =>
        Jurors.Any(j => j.Court == court && j.Seat == juryNumber) ||
        (court == Court && Summoned.Values.Contains(juryNumber));

    /// <summary>
    /// The first chair of this trial's courtroom floor row that nobody holds. The client's table holds
    /// <see cref="TrialSeatRules.SeatsPerBank"/> chairs per bank, and a trial hears its case on the
    /// floor bank, so that is the bench. Null when every chair is taken or promised.
    /// </summary>
    public int? NextFreeSeat()
    {
        for (var seat = 0; seat < TrialSeatRules.SeatsPerBank; seat++)
        {
            if (!IsSeatTaken(Court, seat))
                return seat;
        }

        return null;
    }

    /// <summary>The seat to send the court, or null when the number names no chair in this courtroom.</summary>
    public TrialSeatRules.BenchSeat? ResolveBench(int juryNumber)
    {
        if (juryNumber < 0 || juryNumber >= TrialSeatRules.SeatsPerBank)
            return null;

        return TrialSeatRules.GetGroundBench(Court, juryNumber);
    }

    /// <summary>How many jurors have finished reading the record.</summary>
    public int ReadRecordCount => Jurors.Count(j => j.ReadRecord);

    /// <summary>How many jurors have voted.</summary>
    public int VotedCount => Jurors.Count(j => j.Voted);
}
