using System.Security.Cryptography;

namespace AAEmu.Game.Models.Game.SecondPassword;

/// <summary>
/// The scrambled key tables the client shows for the second password, and the mapping between what the
/// player clicks and the password itself.
/// <para>
/// The client is handed a set of tables, each a permutation of <see cref="Alphabet"/>, and draws the
/// password window from one of them, so the click a player makes says nothing on its own. The client sends
/// back the clicked positions, one character per click, each encoded as the position's character in
/// <see cref="Alphabet"/>; reading the password back needs the same table the player was looking at.
/// </para>
/// </summary>
public static class SecondPasswordKeyTable
{
    /// <summary>How many tables a client is handed in one answer.</summary>
    public const int TableCount = 4;

    /// <summary>
    /// The characters a table permutes: digits, upper case, lower case — 62 of them, which is also the
    /// length the client reads a table with.
    /// </summary>
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// <see cref="TableCount"/> freshly shuffled tables, each a permutation of <see cref="Alphabet"/>.
    /// </summary>
    public static string[] Build() => Build(RandomNumberGenerator.GetInt32);

    /// <summary>
    /// The same shuffle, drawing each swap from the caller's source of indices. A test pins the result by
    /// passing a counted sequence; the server passes the cryptographic generator.
    /// </summary>
    /// <remarks>
    /// The tables are handed to the client in the clear, so the shuffle is not what keeps the password
    /// secret — but the indices cost nothing to draw from a cryptographic source, and a predictable
    /// permutation in a file about password entry invites every reader to work out whether it matters.
    /// </remarks>
    public static string[] Build(Func<int, int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(nextIndex);
        var tables = new string[TableCount];
        for (var i = 0; i < TableCount; i++)
        {
            var chars = Alphabet.ToCharArray();
            for (var j = chars.Length - 1; j > 0; j--)
            {
                var swap = nextIndex(j + 1);
                (chars[j], chars[swap]) = (chars[swap], chars[j]);
            }

            tables[i] = new string(chars);
        }

        return tables;
    }

    /// <summary>
    /// The password behind a sequence of clicked positions, or null when the table is unusable or a click
    /// is not a position this alphabet can name.
    /// </summary>
    public static string Decode(string table, string clickedPositions)
    {
        if (string.IsNullOrEmpty(table) || clickedPositions == null)
            return null;

        var password = new char[clickedPositions.Length];
        for (var i = 0; i < clickedPositions.Length; i++)
        {
            var position = Alphabet.IndexOf(clickedPositions[i]);
            if (position < 0 || position >= table.Length)
                return null; // not a position the client could have drawn
            password[i] = table[position];
        }

        return new string(password);
    }

    /// <summary>
    /// The positions a player would have to click on <paramref name="table"/> to enter
    /// <paramref name="password"/>, or null when a character is not in the table.
    /// </summary>
    public static string Encode(string table, string password)
    {
        if (string.IsNullOrEmpty(table) || password == null)
            return null;

        var positions = new char[password.Length];
        for (var i = 0; i < password.Length; i++)
        {
            var position = table.IndexOf(password[i]);
            if (position < 0)
                return null;
            positions[i] = Alphabet[position];
        }

        return new string(positions);
    }
}
