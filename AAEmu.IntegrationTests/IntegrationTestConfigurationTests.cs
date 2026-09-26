using Microsoft.Extensions.Configuration;
using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>
/// Covers the guard that replaces the opaque "%db_port% is not a valid value for UInt16" binder error
/// with a message naming the keys and the file to fix.
/// </summary>
public sealed class IntegrationTestConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aaemu-itest-config-" + Guid.NewGuid().ToString("N"));

    public IntegrationTestConfigurationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_root, name), content);

    private const string TemplateWithWildcards = """
    {
      "Connections": {
        "MySQLProvider": {
          "Host": "%db_host%",
          "Port": "%db_port%",
          "User": "%db_user%",
          "Password": "%db_password%",
          "Database": "aaemu_game"
        }
      },
      "LoginNetwork": { "Host": "%login_host%", "Port": "%login_port%" }
    }
    """;

    [Fact]
    public void Build_WhenBaseConfigKeepsWildcards_ThrowsAndNamesTheKeysAndTheFix()
    {
        Write(IntegrationTestConfiguration.BaseConfigFileName, TemplateWithWildcards);

        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationTestConfiguration.Build(_root));

        // The message must identify the actual cause, not just the conversion failure.
        Assert.Contains("Connections:MySQLProvider:Port", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Connections:MySQLProvider:Host", ex.Message, StringComparison.Ordinal);
        Assert.Contains(IntegrationTestConfiguration.LocalConfigFileName, ex.Message, StringComparison.Ordinal);
        Assert.Contains(IntegrationTestConfiguration.LocalConfigExampleFileName, ex.Message, StringComparison.Ordinal);

        // The old symptom: the binder could not turn the literal into a UInt16.
        Assert.DoesNotContain("UInt16", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenLocalOverlayBindsTheWildcards_Succeeds()
    {
        Write(IntegrationTestConfiguration.BaseConfigFileName, TemplateWithWildcards);
        Write(IntegrationTestConfiguration.LocalConfigFileName, """
        {
          "Connections": {
            "MySQLProvider": { "Host": "127.0.0.1", "Port": 3306, "User": "root", "Password": "" }
          },
          "LoginNetwork": { "Host": "127.0.0.1", "Port": 1237 }
        }
        """);

        var configuration = IntegrationTestConfiguration.Build(_root);

        // This is the exact bind that used to throw InvalidOperationException.
        var settings = new AAEmu.Commons.Models.MySqlConnectionSettings();
        configuration.GetSection("Connections:MySQLProvider").Bind(settings);
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Equal((ushort)3306, settings.Port);
        Assert.Equal("root", settings.User);
        Assert.Empty(IntegrationTestConfiguration.FindUnboundWildcards(configuration));
    }

    [Fact]
    public void Build_WhenConfigurationsOverlayIsPresent_ItIsLoaded()
    {
        Write(IntegrationTestConfiguration.BaseConfigFileName, TemplateWithWildcards);
        Write(IntegrationTestConfiguration.LocalConfigFileName, """
        {
          "Connections": {
            "MySQLProvider": { "Host": "127.0.0.1", "Port": 3306, "User": "root", "Password": "" }
          },
          "LoginNetwork": { "Host": "127.0.0.1", "Port": 1237 }
        }
        """);
        Directory.CreateDirectory(Path.Combine(_root, IntegrationTestConfiguration.ConfigurationsDirectoryName));
        Write(Path.Combine(IntegrationTestConfiguration.ConfigurationsDirectoryName, "Zone.json"),
            """{ "LoginNetwork": { "Host": "overlay-host", "Port": 4321 } }""");

        var configuration = IntegrationTestConfiguration.Build(_root);

        // Configurations/ is applied after Config.json but before Config.Local.json, so the overlay
        // wins over Config.json and Config.Local.json wins over both.
        Assert.Equal("127.0.0.1", configuration["LoginNetwork:Host"]);
        Assert.Equal("1237", configuration["LoginNetwork:Port"]);
        Assert.Equal("127.0.0.1", configuration["Connections:MySQLProvider:Host"]);
    }

    [Fact]
    public void Build_WhenTheLocalOverlayLeavesOneWildcard_StillFails()
    {
        Write(IntegrationTestConfiguration.BaseConfigFileName, TemplateWithWildcards);
        // Deliberately incomplete: the overlay binds everything except the password.
        Write(IntegrationTestConfiguration.LocalConfigFileName, """
        {
          "Connections": {
            "MySQLProvider": { "Host": "127.0.0.1", "Port": 3306, "User": "root" }
          },
          "LoginNetwork": { "Host": "127.0.0.1", "Port": 1237 }
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationTestConfiguration.Build(_root));

        // A partial overlay must not be mistaken for a working one.
        Assert.Contains("Connections:MySQLProvider:Password", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenBaseConfigIsMissing_ExplainsHowToProvideIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationTestConfiguration.Build(_root));

        Assert.Contains(IntegrationTestConfiguration.BaseConfigFileName, ex.Message, StringComparison.Ordinal);
        Assert.Contains(IntegrationTestConfiguration.LocalConfigExampleFileName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindUnboundWildcards_IgnoresValuesThatOnlyContainAPercentSign()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Connections:MySQLProvider:Database"] = "aaemu_game",
                ["CharacterNameRegex"] = "^[a-z]{1,18}$",
                ["World:MOTD"] = "100% complete",
                ["Connections:MySQLProvider:User"] = "%db_user%",
            })
            .Build();

        var unbound = IntegrationTestConfiguration.FindUnboundWildcards(configuration);

        // Only a whole-value wildcard counts; a percent sign inside real text is fine.
        Assert.Equal(["Connections:MySQLProvider:User"], unbound);
    }

    [Fact]
    public void RequireCompactDatabase_WhenTheContentDatabaseIsAbsent_ExplainsHowToProvideIt()
    {
        // Pointed at an empty root on purpose: whether the real test output happens to carry the
        // content database must not decide whether this guard works.
        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationTestConfiguration.RequireCompactDatabase(_root));

        Assert.Contains(IntegrationTestConfiguration.CompactDatabaseFileName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("readme.md", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireCompactDatabase_WhenTheContentDatabaseIsPresent_ReturnsItsPath()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Data"));
        var expected = Path.Combine(_root, "Data", IntegrationTestConfiguration.CompactDatabaseFileName);
        File.WriteAllText(expected, "not really a database");

        Assert.Equal(expected, IntegrationTestConfiguration.RequireCompactDatabase(_root));
    }
}
