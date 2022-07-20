using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Utils.DB
{
    public class SQLiteWrapperReader : IDisposable
    {
        private readonly SqliteDataReader _reader;
        private readonly Dictionary<string, int> _ordinal;

        public SQLiteWrapperReader(SqliteDataReader reader)
        {
            _reader = reader;
            _ordinal = new Dictionary<string, int>();
        }

        public bool Read() => _reader.Read();

        public object GetValue(string column)
        {
            return _reader.GetValue(GetOrdinal(column));
        }

        public bool GetBoolean(string column)
        {
            return _reader.GetBoolean(GetOrdinal(column));
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
            return _reader.GetByte(GetOrdinal(column));
        }

        public byte GetByte(string column, byte defaultValue)
        {
            var ordinal = GetOrdinal(column);
            if (_reader.IsDBNull(ordinal))
                return defaultValue;
            return _reader.GetByte(ordinal);
        }

        public long GetBytes(string column, long fieldOffset, byte[] buffer, int bufferOffset, int length)
        {
            return _reader.GetBytes(GetOrdinal(column), fieldOffset, buffer, bufferOffset, length);
        }

        public char GetChar(string column)
        {
            return _reader.GetChar(GetOrdinal(column));
        }

        public long GetChars(string column, long fieldOffset, char[] buffer, int bufferOffset, int length)
        {
            return _reader.GetChars(GetOrdinal(column), fieldOffset, buffer, bufferOffset, length);
        }

        public Guid GetGuid(string column)
        {
            return _reader.GetGuid(GetOrdinal(column));
        }

        public short GetInt16(string column)
        {
            return _reader.GetInt16(GetOrdinal(column));
        }

        public ushort GetUInt16(string column) => (ushort) GetInt16(column);

        public int GetInt32(string column)
        {
            //Same impl of Sqlite.Core v2.2.1
            return (int)_reader.GetInt64(GetOrdinal(column));
        }

        public int GetInt32(string column, int defaultValue)
        {
            var ordinal = GetOrdinal(column);
            if (_reader.IsDBNull(ordinal))
                return defaultValue;

            //Same impl of Sqlite.Core v2.2.1
            return (int)_reader.GetInt64(ordinal);
        }

        public uint GetUInt32(string column) => (uint) GetInt32(column);

        public uint GetUInt32(string column, uint defaultValue)
        {
            var ordinal = GetOrdinal(column);
            if (_reader.IsDBNull(ordinal))
                return defaultValue;
            return (uint) GetInt32(column);
        }

        public long GetInt64(string column)
        {
            return _reader.GetInt64(GetOrdinal(column));
        }

        public ulong GetUInt64(string column) => (ulong) GetInt64(column);

        public float GetFloat(string column)
        {
            return _reader.GetFloat(GetOrdinal(column));
        }

        public float GetFloat(string column, float defaultValue)
        {
            var ordinal = GetOrdinal(column);
            if (_reader.IsDBNull(ordinal))
                return defaultValue;
            return _reader.GetFloat(ordinal);
        }

        public double GetDouble(string column)
        {
            return _reader.GetDouble(GetOrdinal(column));
        }

        public string GetString(string column)
        {
            return _reader.GetString(GetOrdinal(column));
        }

        public string GetString(string column, string defaultValue)
        {
            var ordinal = GetOrdinal(column);
            if (_reader.IsDBNull(ordinal))
                return defaultValue;
            return _reader.GetString(ordinal);
        }

        public decimal GetDecimal(string column)
        {
            return _reader.GetDecimal(GetOrdinal(column));
        }

        public DateTime GetDateTime(string column)
        {
            return _reader.GetDateTime(GetOrdinal(column));
        }

        public bool IsDBNull(string column)
        {
            return _reader.IsDBNull(GetOrdinal(column));
        }

        public int GetOrdinal(string column)
        {
            if (_ordinal.ContainsKey(column))
                return _ordinal[column];

            var ordinal = _reader.GetOrdinal(column);
            _ordinal.Add(column, ordinal);
            return ordinal;
        }

        public void Dispose()
        {
            _ordinal.Clear();
            _reader.Dispose();
        }
    }
}
