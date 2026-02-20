using Moq;

namespace AAEmu.UnitTests;

/// <summary>
/// Base class for all unit tests providing common fixtures and mock objects
/// </summary>
public abstract class TestBase
{
    /// <summary>
    /// Common mock for ILogger usage
    /// </summary>
    protected Mock<object> MockLogger { get; }

    /// <summary>
    /// Common mock for database connection
    /// </summary>
    protected Mock<Microsoft.Data.Sqlite.SqliteConnection> MockDbConnection { get; }

    protected TestBase()
    {
        MockLogger = new Mock<object>();
        MockDbConnection = new Mock<Microsoft.Data.Sqlite.SqliteConnection>();
    }
}
