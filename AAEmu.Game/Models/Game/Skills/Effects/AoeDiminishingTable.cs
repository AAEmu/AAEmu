namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The ten <c>aoe_diminishings</c> rows, in id order: the rate each successive hit of an area skill takes.
/// </summary>
/// <remarks>
/// Loaded from content into <see cref="Rates"/>; empty means the table was never loaded, and every caller
/// treats that as "no diminishing" rather than a rate of zero. 10.0.2.13 ships ids 1..10 with rates 100, 95,
/// 90, 85, 80, 75, 70, 65, 60, 50.
/// </remarks>
public static class AoeDiminishingTable
{
    private static readonly List<(uint Id, int Rate)> Ordered = [];

    public static IReadOnlyList<int> Rates { get; private set; } = [];

    public static void Clear() => Ordered.Clear();

    public static void Add(uint id, int rate) => Ordered.Add((id, rate));

    /// <summary>
    /// Freezes the loaded rows into the rate list, ordered by id so the sequence is the content's.
    /// </summary>
    public static void Seal() =>
        Rates = Ordered.OrderBy(row => row.Id).Select(row => row.Rate).ToList();

    public static bool IsLoaded => Rates.Count > 0;
}
