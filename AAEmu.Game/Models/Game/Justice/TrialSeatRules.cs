namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// Where a trial's benches are. The placed juror chairs (doodad templates 4937-4956, 배심원석) sit as two
/// banks of five at each courtroom: the Marianople court (next to the 재판소 doodads 6123/6124) and the
/// eastern court (next to 6122). The lower row of a court is sent as the west bank, the raised one as the
/// east bank - the client only mirrors the (court, seat) pair it is told, so the pairing is ours to keep.
/// </summary>
public static class TrialSeatRules
{
    public const int SeatsPerBank = 5;

    public readonly record struct Seat(float X, float Y, float Z);

    // Marianople court, lower row (doodads 4937-4941).
    private static readonly Seat[] MarianopleWest =
    [
        new(11102.2f, 12082.2f, 146.2f), new(11103.3f, 12081.7f, 146.2f), new(11104.4f, 12081.2f, 146.2f),
        new(11105.5f, 12080.6f, 146.2f), new(11106.6f, 12080.1f, 146.2f)
    ];

    // Marianople court, raised row (doodads 4942-4946).
    private static readonly Seat[] MarianopleEast =
    [
        new(11102.5f, 12082.8f, 156.8f), new(11103.6f, 12082.3f, 156.8f), new(11104.7f, 12081.7f, 156.8f),
        new(11105.8f, 12081.2f, 156.8f), new(11106.9f, 12080.7f, 156.7f)
    ];

    // Eastern court, row A (doodads 4947-4951).
    private static readonly Seat[] EasternWest =
    [
        new(17032.9f, 8903.6f, 157.8f), new(17033.0f, 8902.6f, 157.8f), new(17033.0f, 8901.6f, 157.8f),
        new(17033.0f, 8900.6f, 157.8f), new(17033.0f, 8899.7f, 157.8f)
    ];

    // Eastern court, row B (doodads 4952-4956).
    private static readonly Seat[] EasternEast =
    [
        new(17027.5f, 8916.6f, 148.0f), new(17027.5f, 8915.6f, 148.0f), new(17027.5f, 8914.7f, 148.0f),
        new(17027.5f, 8913.7f, 148.0f), new(17027.5f, 8912.7f, 148.0f)
    ];

    /// <summary>
    /// The seat position for a (court, bank, number) triple, or null when the combination has no bench -
    /// callers refuse instead of dropping a juror at a guessed spot.
    /// </summary>
    public static Seat? GetSeat(int court, bool isWest, int juryNumber)
    {
        if (juryNumber is < 0 or >= SeatsPerBank)
            return null;

        return court switch
        {
            0 => isWest ? MarianopleWest[juryNumber] : MarianopleEast[juryNumber],
            1 => isWest ? EasternWest[juryNumber] : EasternEast[juryNumber],
            _ => null
        };
    }

    /// <summary>
    /// Court 0 is the Marianople (Nuia) courtroom, court 1 the eastern one.
    /// </summary>
    public static int CourtForNation(bool nuia) => nuia ? 0 : 1;

    /// <summary>
    /// One bench seat: the courtroom, the bank of chairs inside it, the chair number and where that
    /// chair stands. The bank flag travels with the position because the client draws the juror in the
    /// bank it is told, so a position from one row with the flag of the other puts the juror in the
    /// gallery while the server has them on the floor.
    /// </summary>
    public readonly record struct BenchSeat(int Court, bool IsWest, int JuryNumber, Seat Position);

    /// <summary>
    /// The bench seat for a chair on a courthouse floor row. A court's two banks are one floor row and
    /// one raised gallery, and the gallery is not where a trial is heard, so the floor bank is
    /// whichever of the two sits lower - which is the west bank at Marianople and the east bank at the
    /// eastern court. Null when the combination has no chair.
    /// </summary>
    public static BenchSeat? GetGroundBench(int court, int juryNumber)
    {
        var west = GetSeat(court, isWest: true, juryNumber);
        var east = GetSeat(court, isWest: false, juryNumber);

        if (west is not { } westSeat)
            return east is { } eastOnly
                ? new BenchSeat(court, false, juryNumber, eastOnly)
                : null;

        if (east is not { } eastSeat)
            return new BenchSeat(court, true, juryNumber, westSeat);

        return eastSeat.Z < westSeat.Z
            ? new BenchSeat(court, false, juryNumber, eastSeat)
            : new BenchSeat(court, true, juryNumber, westSeat);
    }

    /// <summary>
    /// The courtroom floor row. A court's two banks share their ground position, one of them raised:
    /// the raised row is the gallery, so the floor is whichever bank sits lower.
    /// </summary>
    public static Seat? GetGroundSeat(int court, int juryNumber)
    {
        var west = GetSeat(court, isWest: true, juryNumber);
        var east = GetSeat(court, isWest: false, juryNumber);

        if (west is not { } first)
            return east;
        if (east is not { } second)
            return first;

        return second.Z < first.Z ? second : first;
    }
}
