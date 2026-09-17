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
    /// The values of the given holders on one board in one window, by holder id, leaving out the holders
    /// who are not on it. A board over expeditions is built from its members' values on another board, and
    /// this reads exactly those members rather than the whole board.
    /// </summary>
    Dictionary<ulong, long> ReadValues(uint rankId, DateTime periodStartUtc, IReadOnlyCollection<ulong> holderIds);

    /// <summary>
    /// Adds what a character gained or spent to the running totals of a window.
    /// </summary>
    void AddGamePointTotals(MySqlConnection connection, MySqlTransaction transaction, RankScore holder,
        DateTime periodStartUtc, IReadOnlyDictionary<(int Kind, int Method), long> totals, DateTime updatedAtUtc);

    /// <summary>What a character has gained or spent in a window, or 0 when nothing has been counted.</summary>
    long ReadGamePointTotal(ulong characterId, int kind, int method, DateTime periodStartUtc);

    /// <summary>
    /// Every character's total for one running total in one window, so a period board can be built from
    /// what is stored rather than from whoever happens to be in world.
    /// </summary>
    List<RankScore> ReadGamePointBoard(uint rankId, int kind, int method, DateTime periodStartUtc);

    /// <summary>Whether a board's window has already been paid out.</summary>
    bool HasPayout(uint rankId, DateTime periodStartUtc);

    /// <summary>Records that a board's window has been paid, so it is paid once.</summary>
    void MarkPayout(uint rankId, DateTime periodStartUtc, DateTime paidAtUtc);
}
