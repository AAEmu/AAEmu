using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace AAEmu.Login.Core.Controllers;

public partial class LoginController(
    IGameController gameController,
    IOptions<AppConfiguration> appConfig,
    IMySqlConnectionFactory connectionFactory,
    ILogger<LoginController> logger) : ILoginController
{
    private readonly bool _autoAccount = appConfig.Value.AutoAccount;

    private readonly ConcurrentDictionary<GameServerId, ConcurrentDictionary<uint, AccountId>>
        _tokens = []; // gsId, [token, accountId]

    // Allows Unicode letters and digits (any script), plus _ . - @. No control characters or newlines.
    [GeneratedRegex(@"^[\p{L}\p{Nd}_.\-@]{1,32}$")]
    private static partial Regex UsernameRegex();

    /// <summary>
    /// Kr Method Auth
    /// </summary>
    public async Task<LoginResult> Login(string username)
    {
        await using var connect = connectionFactory.CreateConnection();
        await using var command = connect.CreateCommand();
        command.CommandText = "SELECT * FROM users where username=@username";
        command.Parameters.AddWithValue("@username", username);
        await using var reader = command.ExecuteReader();
        if (!await reader.ReadAsync())
        {
            return new LoginResult(false, default, LoginDeniedReason.BadAccount);
        }

        // TODO ... validation password

        var accountId = new AccountId(reader.GetUInt32("id"));
        var lastLogin = DateTime.UtcNow;

        await reader.CloseAsync();

        #region update account

        command.Parameters.Clear();
        command.CommandText =
            "UPDATE `users` SET last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
        command.Parameters.AddWithValue("@id", accountId.Value);
        command.Parameters.AddWithValue("@last_ip", "");
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)lastLogin).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)lastLogin).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            logger.LogWarning("Database update failed, error occurred while updating account login IP and time");
        }

        # endregion

        return new LoginResult(true, accountId, default);
    }

    /// <summary>
    /// Eu Method Auth
    /// </summary>
    public async Task<LoginResult> Login(string username, string password, IPAddress ip,
        CancellationToken cancellationToken)
    {
        await using var connect = connectionFactory.CreateConnection();
        await using var command = connect.CreateCommand();
        command.CommandText = "SELECT * FROM users where username=@username";
        command.Parameters.AddWithValue("@username", username);
        await using var reader = command.ExecuteReader();
        if (!await reader.ReadAsync())
        {
            if (_autoAccount)
            {
                await reader.CloseAsync();
                return await CreateAndLoginInvalid(username, password, ip, connect);
            }

            return new LoginResult(false, default, LoginDeniedReason.BadResponse);
        }

        var storedPassword = reader.GetString("password");
        if (password != storedPassword)
        {
            return new LoginResult(false, default, LoginDeniedReason.BadResponse);
        }

        var banned = reader.GetBoolean("banned");
        if (banned)
        {
            var banReason = (LoginDeniedReason)(byte)reader.GetUInt32("ban_reason");
            return new LoginResult(false, default, banReason);
        }

        var accountId = new AccountId(reader.GetUInt32("id"));
        var lastLogin = DateTime.UtcNow;

        logger.LogInformation("{Username} connected.", username.ReplaceLineEndings(" "));

        await reader.CloseAsync();

        #region update account

        command.Parameters.Clear();
        command.CommandText =
            "UPDATE `users` SET last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
        command.Parameters.AddWithValue("@id", accountId.Value);
        command.Parameters.AddWithValue("@last_ip", ip);
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)lastLogin).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)lastLogin).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            logger.LogWarning("Database update failed, error occurred while updating account login IP and time");
        }

        # endregion

        return new LoginResult(true, accountId, default);
    }

    private async Task<LoginResult> CreateAndLoginInvalid(string username, string password, IPAddress clientIp,
        MySqlConnection connection)
    {
        if (!UsernameRegex().IsMatch(username))
            return new LoginResult(false, default, LoginDeniedReason.BadAccount);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT into users (username, password, email, last_ip, last_login, created_at, updated_at) VALUES (@username, @password, @email, @last_ip, @last_login, @created_at, @updated_at)";
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@password", password);
        command.Parameters.AddWithValue("@email", "");
        command.Parameters.AddWithValue("@last_ip", clientIp);
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@created_at", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            return new LoginResult(false, default, LoginDeniedReason.BadResponse);
        }

        logger.LogDebug("Created account from invalid username login with value {Username}", username);
        return await Login(username, password, clientIp, CancellationToken.None);
    }

    public void AddReconnectionToken(InternalConnection connection, GameServerId gsId, AccountId accountId, uint token)
    {
        var tokensForGameServer = _tokens.GetOrAdd(gsId, static _ => []);
        tokensForGameServer.TryAdd(token, accountId);
        connection.SendPacket(new LGPlayerReconnectPacket(token));
    }

    public Task<ReconnectResult> Reconnect(GameServerId gsId, AccountId accountId, uint token)
    {
        if (!_tokens.ContainsKey(gsId))
        {
            if (gameController.TryGetParentId(gsId, out var parentId))
                gsId = parentId;
            else
            {
                // TODO ...
                return Task.FromResult(new ReconnectResult(false, default));
            }
        }

        if (!_tokens[gsId].TryGetValue(token, out var value))
        {
            // TODO ...
            return Task.FromResult(new ReconnectResult(false, default));
        }

        if (value == accountId)
        {
            return Task.FromResult(new ReconnectResult(true, accountId));
        }

        // TODO ...
        return Task.FromResult(new ReconnectResult(false, default));
    }
}
