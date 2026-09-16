namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>
/// The places a board hands out, decided here so the ordering and the tie rule are one tested thing.
/// </summary>
public static class RankScoreboard
{
    /// <summary>
    /// Orders holders by value, best first, and gives each the place it holds.
    /// </summary>
    /// <param name="scores">The holders' values for one board and one window.</param>
    /// <param name="permitTie">
    /// Whether equal values share a place (<c>ranks.permit_tie</c>). A board that does not permit ties
    /// still orders them, but each holder is given the next place instead of the same one.
    /// </param>
    public static List<RankPlace> Place(IEnumerable<RankScore> scores, bool permitTie)
    {
        var ordered = (scores ?? [])
            .OrderByDescending(score => score.Value)
            .ThenBy(score => score.HolderId)
            .ToList();

        var places = new List<RankPlace>(ordered.Count);
        uint position = 0;
        long? previousValue = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var score = ordered[i];
            if (permitTie && previousValue == score.Value)
            {
                places.Add(new RankPlace(score, position));
                continue;
            }

            position = (uint)i + 1;
            previousValue = score.Value;
            places.Add(new RankPlace(score, position));
        }

        return places;
    }
}
