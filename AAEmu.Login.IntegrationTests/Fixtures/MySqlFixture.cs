using AAEmu.Commons.Models;
using AAEmu.Commons.Utils.DB;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Xunit;

namespace AAEmu.Login.IntegrationTests.Fixtures;

public class MySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithDatabase("aaemu_login")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "SQL", "aaemu_login.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath);

        // Strip CREATE DATABASE / USE lines — Testcontainers already created the DB
        var filteredLines = schemaSql
            .Split('\n')
            .Where(line =>
            {
                var trimmed = line.TrimStart().ToUpperInvariant();
                return !trimmed.StartsWith("CREATE DATABASE") && !trimmed.StartsWith("USE ");
            });
        var cleanedSql = string.Join('\n', filteredLines);

        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = cleanedSql;
        await command.ExecuteNonQueryAsync();

        // Point the static MySQL connection factory at the container
        var connectionString = _container.GetConnectionString();
        var builder = new MySqlConnectionStringBuilder(connectionString);
        MySQL.SetConfiguration(new MySqlConnectionSettings
        {
            Host = builder.Server,
            Port = (ushort)builder.Port,
            User = builder.UserID,
            Password = builder.Password,
            Database = builder.Database
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("MySql")]
public class MySqlCollection : ICollectionFixture<MySqlFixture>;
