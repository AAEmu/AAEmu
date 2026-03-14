using System.Net;
using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAEmu.Login.IntegrationTests;

[Collection("MySql")]
public class LoginControllerIntegrationTests : IAsyncLifetime
{
    private readonly IPAddress _ipAddress = IPAddress.Parse("127.0.0.1");

    public async ValueTask InitializeAsync()
    {
        await using var connection = MySQL.CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE users;";
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static LoginController CreateController(bool autoAccount = false)
    {
        var gameController = new Mock<IGameController>();
        var passwordService = new Mock<IPasswordService>();
        passwordService.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<Password>()))
            .Returns((string stored, Password provided) =>
                stored == provided.Value ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed);
        passwordService.Setup(p => p.HashForStorage(It.IsAny<Password>()))
            .Returns((Password p) => p.Value);
        var appConfig = Options.Create(new AppConfiguration
        {
            SecretKey = "test-key", AutoAccount = autoAccount, GameServers = []
        });
        var connectionFactory = new Mock<IMySqlConnectionFactory>();
        connectionFactory.Setup(f => f.CreateConnection()).Returns(MySQL.CreateConnection);
        var logger = new Mock<ILogger<LoginController>>();
        var koreaOptions = Options.Create(new KoreaChallengeAuthOptions());
        return new LoginController(gameController.Object, passwordService.Object, appConfig, koreaOptions,
            connectionFactory.Object, logger.Object);
    }

    private static async Task InsertUser(string username, string password, bool banned = false, int banReason = 0)
    {
        await using var connection = MySQL.CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users (username, password, email, last_ip, last_login, created_at, updated_at, banned, ban_reason) " +
            "VALUES (@username, @password, '', '', 0, 0, 0, @banned, @ban_reason)";
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@password", password);
        cmd.Parameters.AddWithValue("@banned", banned ? 1 : 0);
        cmd.Parameters.AddWithValue("@ban_reason", banReason);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Login_NonExistentUser_AutoAccountOff_ReturnsFailed()
    {
        var controller = CreateController(autoAccount: false);

        var result = await controller.Login("no_such_user", Password.FromPlaintext("pass"), _ipAddress,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(LoginDeniedReason.BadAccount, result.DenialReason);
    }

    [Fact]
    public async Task Login_NonExistentUser_AutoAccountOn_CreatesAndSucceeds()
    {
        var controller = CreateController(autoAccount: true);

        var result = await controller.Login("autouser", Password.FromPlaintext("newpass"), _ipAddress,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotEqual(default, result.AccountId);
    }

    [Fact]
    public async Task Login_CorrectPassword_ReturnsSuccess()
    {
        await InsertUser("alice", "mypassword");
        var controller = CreateController();

        var result =
            await controller.Login("alice", Password.FromPlaintext("mypassword"), _ipAddress,
                TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsFailed()
    {
        await InsertUser("bob", "correctpass");
        var controller = CreateController();

        var result = await controller.Login("bob", Password.FromPlaintext("wrongpass"), _ipAddress,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(LoginDeniedReason.BadAccount, result.DenialReason);
    }

    [Fact]
    public async Task Login_BannedUser_ReturnsBanReason()
    {
        await InsertUser("banned_user", "pass", banned: true, banReason: 5);
        var controller = CreateController();

        var result = await controller.Login("banned_user", Password.FromPlaintext("pass"), _ipAddress,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(LoginDeniedReason.TryTradeCashTemporal, result.DenialReason);
    }

    [Fact]
    public async Task Login_UpdatesLastLoginAndIp()
    {
        await InsertUser("tracker", "pass");
        var controller = CreateController();

        var result = await controller.Login("tracker", Password.FromPlaintext("pass"), _ipAddress,
            TestContext.Current.CancellationToken);
        Assert.True(result.Success);

        // Verify the DB was updated
        await using var connection = MySQL.CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT last_ip, last_login, updated_at FROM users WHERE username=@username";
        cmd.Parameters.AddWithValue("@username", "tracker");
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(_ipAddress, IPAddress.Parse(reader["last_ip"].ToString()!));

        var lastLogin = Convert.ToInt64(reader["last_login"]);
        Assert.True(lastLogin > 0, "last_login should be updated to a non-zero timestamp");

        var updatedAt = Convert.ToInt64(reader["updated_at"]);
        Assert.True(updatedAt > 0, "updated_at should be updated to a non-zero timestamp");
    }
}
