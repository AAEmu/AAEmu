using System.Data;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests;

/// <summary>
/// Base class for integration tests that use the real compact.sqlite3 database
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected SqliteConnection Connection { get; }
    protected string DatabasePath { get; }

    protected IntegrationTestBase()
    {
        // Use the real compact database
        DatabasePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", 
            "AAEmu.Game", "Data", "compact.sqlite3");
        
        Connection = new SqliteConnection($"Data Source={DatabasePath}");
        Connection.Open();
    }

    /// <summary>
    /// Disposes managed resources
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false if called from finalizer</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Connection?.Close();
            Connection?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
