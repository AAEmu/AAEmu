using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Utils.DB;

public sealed class SQLiteWrapperReader(SqliteDataReader reader) : IDisposable
{
    private readonly Dictionary<string, int> _ordinal = [];

    public bool Read() => reader.Read();

    public object GetValue(string column)
    {
        return reader.GetValue(GetOrdinal(column));
    }

    public bool GetBoolean(string column)
    {
        return reader.GetBoolean(GetOrdinal(column));
    }

    public bool GetBoolean(string column, bool fromString)
    {
        if (fromString)
        {
            if (IsDBNull(column))
                return false;

            var value = GetString(column);
            return value == "t" || value == "1";
        }

        return GetBoolean(column);
    }

    public byte GetByte(string column)
    {
        return reader.GetByte(GetOrdinal(column));
    }

    public byte GetByte(string column, byte defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return reader.GetByte(ordinal);
    }

    public long GetBytes(string column, long fieldOffset, byte[] buffer, int bufferOffset, int length)
    {
        return reader.GetBytes(GetOrdinal(column), fieldOffset, buffer, bufferOffset, length);
    }

    public char GetChar(string column)
    {
        return reader.GetChar(GetOrdinal(column));
    }

    public long GetChars(string column, long fieldOffset, char[] buffer, int bufferOffset, int length)
    {
        return reader.GetChars(GetOrdinal(column), fieldOffset, buffer, bufferOffset, length);
    }

    public Guid GetGuid(string column)
    {
        return reader.GetGuid(GetOrdinal(column));
    }

    public short GetInt16(string column)
    {
        return reader.GetInt16(GetOrdinal(column));
    }

    public ushort GetUInt16(string column) => (ushort)GetInt16(column);

    public int GetInt32(string column)
    {
        //Same impl of Sqlite.Core v2.2.1
        return (int)reader.GetInt64(GetOrdinal(column));
    }

    public int GetInt32(string column, int defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;

        //Same impl of Sqlite.Core v2.2.1
        return (int)reader.GetInt64(ordinal);
    }

    public uint GetUInt32(string column) => (uint)GetInt32(column);

    public uint GetUInt32(string column, uint defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return (uint)GetInt32(column);
    }

    public long GetInt64(string column)
    {
        return reader.GetInt64(GetOrdinal(column));
    }

    public ulong GetUInt64(string column) => (ulong)GetInt64(column);

    public float GetFloat(string column)
    {
        return reader.GetFloat(GetOrdinal(column));
    }

    public float GetFloat(string column, float defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return reader.GetFloat(ordinal);
    }

    public double GetDouble(string column)
    {
        return reader.GetDouble(GetOrdinal(column));
    }

    public string GetString(string column)
    {
        return reader.GetString(GetOrdinal(column));
    }

    public string GetString(string column, string defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return reader.GetString(ordinal);
    }

    public decimal GetDecimal(string column)
    {
        return reader.GetDecimal(GetOrdinal(column));
    }

    public DateTime GetDateTime(string column)
    {
        return reader.GetDateTime(GetOrdinal(column));
    }

    public bool IsDBNull(string column)
    {
        return reader.IsDBNull(GetOrdinal(column));
    }

    public int GetOrdinal(string column)
    {
        if (_ordinal.TryGetValue(column, out var ordinal1))
            return ordinal1;

        var ordinal = reader.GetOrdinal(column);
        _ordinal.Add(column, ordinal);
        return ordinal;
    }

    public void Dispose()
    {
        _ordinal.Clear();
        reader.Dispose();
    }

    public List<string> GetColumnNames()
    {
        var res = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            res.Add(reader.GetName(i));
        return res;
    }
}
