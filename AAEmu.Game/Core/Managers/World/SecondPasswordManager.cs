using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Hands out the second password key tables and remembers them, so a password that comes back as clicked
/// positions can be read. The tables are per account and are replaced whenever new ones are issued.
/// </summary>
public class SecondPasswordManager : Singleton<SecondPasswordManager>
{
    private sealed record IssuedTables(uint Time, string[] Tables);

    private readonly ConcurrentDictionary<uint, IssuedTables> _issued = new();

    /// <summary>The tables handed out for an account, and the time they were issued with.</summary>
    public string[] Issue(uint accountId, out uint time)
    {
        var tables = SecondPasswordKeyTable.Build(Random.Shared);
        time = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _issued[accountId] = new IssuedTables(time, tables);
        return tables;
    }

    /// <summary>
    /// The password behind clicked positions sent by an account, or null when that account has no tables
    /// outstanding or the positions do not belong to the table they name.
    /// </summary>
    public string Decode(uint accountId, byte tableIndex, string clickedPositions)
    {
        if (!_issued.TryGetValue(accountId, out var issued))
            return null;
        if (tableIndex >= issued.Tables.Length)
            return null;

        return SecondPasswordKeyTable.Decode(issued.Tables[tableIndex], clickedPositions);
    }

    /// <summary>Forgets an account's tables, e.g. once its password has been set or cleared.</summary>
    public void Forget(uint accountId)
    {
        _issued.TryRemove(accountId, out _);
    }
}
