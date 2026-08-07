namespace AAEmu.Web.Models;

/// <summary>
/// A read-only projection of a row in the game database's <c>characters</c> table.
/// </summary>
public sealed class CharacterSummary
{
    public required uint Id { get; init; }
    public required uint AccountId { get; init; }
    public required string Name { get; init; }
    public required int AccessLevel { get; init; }
    public required Race Race { get; init; }
    public required Gender Gender { get; init; }
    public required byte Level { get; init; }
    public required int Experience { get; init; }
    public required long Money { get; init; }
    public required long AaPoint { get; init; }
    public required int HonorPoint { get; init; }
    public required uint FactionId { get; init; }
    public required string FactionName { get; init; }
    public required uint WorldId { get; init; }
    public required uint ZoneId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required bool Deleted { get; init; }

    /// <summary>Total seconds this character has been online.</summary>
    public required uint TotalPlayTime { get; init; }

    /// <summary>
    /// The username from the login database, resolved separately — the two databases can live on
    /// different servers, so they are not joined in SQL. Null when the lookup was not performed.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>Money is stored in copper; the client shows gold/silver/copper.</summary>
    public string FormattedMoney
    {
        get
        {
            var copper = Money;
            var negative = copper < 0;
            copper = Math.Abs(copper);

            var gold = copper / 10000;
            var silver = copper % 10000 / 100;
            var remainder = copper % 100;
            return $"{(negative ? "-" : "")}{gold:N0}g {silver:D2}s {remainder:D2}c";
        }
    }

    public string FormattedPlayTime
    {
        get
        {
            var span = TimeSpan.FromSeconds(TotalPlayTime);
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : $"{span.Minutes}m";
        }
    }
}
