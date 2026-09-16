using MySql.Data.MySqlClient;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Where a board's values live between sessions. A board shows every holder on the server, not only the
/// ones in world, so the figures have to outlive a session.
/// </summary>
public interface IRankScoreStore
{
    /// <summary>Writes the holders' values for the window they were counted in.</summary>
    void Save(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<RankScore> scores);

    /// <summary>The holders of one board in one window, best first, at most <paramref name="limit"/> of them.</summary>
    List<RankScore> ReadBoard(uint rankId, DateTime periodStartUtc, int limit);

    /// <summary>One holder's value on a board in a window, or null when they are not on it.</summary>
    RankScore ReadHolder(uint rankId, DateTime periodStartUtc, RankHolderKind kind, ulong holderId);

    /// <summary>
    /// Adds what a character gained or spent to the running totals of a window.
    /// </summary>
    void AddGamePointTotals(MySqlConnection connection, MySqlTransaction transaction, ulong characterId,
        DateTime periodStartUtc, IReadOnlyDictionary<(int Kind, int Method), long> totals, DateTime updatedAtUtc);

    /// <summary>What a character has gained or spent in a window, or 0 when nothing has been counted.</summary>
    long ReadGamePointTotal(ulong characterId, int kind, int method, DateTime periodStartUtc);
}
