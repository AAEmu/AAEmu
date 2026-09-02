#nullable enable
using System.Text;

namespace AAEmu.Game.Utils.DB;

/// <summary>
/// Parses compact content scripts. A <c>-- compact_table: name</c> marker scopes
/// the following statements so a missing table can be skipped.
/// </summary>
public static class CompactSqlScript
{
    public const string TableMarker = "-- compact_table:";

    public readonly record struct Statement(string? Table, string Sql);

    public static List<Statement> Parse(string script)
    {
        var statements = new List<Statement>();
        if (string.IsNullOrEmpty(script))
            return statements;

        var sql = new StringBuilder();
        var table = (string?)null;
        var inString = false;
        var inLineComment = false;

        void FlushStatement()
        {
            var text = sql.ToString().Trim();
            sql.Clear();
            if (text.Length > 0)
                statements.Add(new Statement(table, text));
        }

        for (var i = 0; i < script.Length; i++)
        {
            var ch = script[i];
            if (inLineComment)
            {
                if (ch is '\n' or '\r')
                    inLineComment = false;
                continue;
            }

            if (!inString && ch == '-' && i + 1 < script.Length && script[i + 1] == '-')
            {
                var rest = PeekLine(script, i);
                if (rest.StartsWith(TableMarker, StringComparison.Ordinal))
                {
                    FlushStatement();
                    table = rest[TableMarker.Length..].Trim();
                    if (table.Length == 0)
                        table = null;
                }

                inLineComment = true;
                i++;
                continue;
            }

            if (ch == '\'')
            {
                sql.Append(ch);
                if (inString && i + 1 < script.Length && script[i + 1] == '\'')
                {
                    sql.Append(script[i + 1]);
                    i++;
                }
                else
                    inString = !inString;
                continue;
            }

            if (!inString && ch == ';')
            {
                FlushStatement();
                continue;
            }

            sql.Append(ch);
        }

        FlushStatement();
        return statements;
    }

    private static string PeekLine(string script, int start)
    {
        var end = start;
        while (end < script.Length && script[end] is not '\n' and not '\r')
            end++;
        return script[start..end].Trim();
    }
}
