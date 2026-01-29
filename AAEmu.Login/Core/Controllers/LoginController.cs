using System.Collections.Concurrent;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace AAEmu.Login.Core.Controllers;

public class LoginController(
    IGameController gameController,
    IOptions<AppConfiguration> appConfig,
    IMySqlConnectionFactory connectionFactory,
    ILogger<LoginController> logger) : ILoginController
{
    private readonly bool _autoAccount = appConfig.Value.AutoAccount;

    private readonly ConcurrentDictionary<GameServerId, ConcurrentDictionary<uint, AccountId>>
        _tokens = []; // gsId, [token, accountId]

    /// <summary>
    /// Kr Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    public async Task Login(ILoginConnection connection, string username)
    {
        await using var connect = connectionFactory.CreateConnection();
        await using var command = connect.CreateCommand();
        command.CommandText = "SELECT * FROM users where username=@username";
        command.Parameters.AddWithValue("@username", username);
        await using var reader = command.ExecuteReader();
        if (!await reader.ReadAsync())
        {
            await connection.SendPacketAsync(new ACLoginDeniedPacket(2), CancellationToken.None);
            return;
        }

        // TODO ... validation password

        connection.AccountId = new AccountId(reader.GetUInt32("id"));
        connection.AccountName = username;
        connection.LastLogin = DateTime.UtcNow;
        connection.LastIp = connection.Ip;

        await connection.SendPacketAsync(new ACJoinResponsePacket(0, 6), CancellationToken.None);
        await connection.SendPacketAsync(new ACAuthResponsePacket(connection.AccountId, 6), CancellationToken.None);

        await reader.CloseAsync();

        #region update account

        command.Parameters.Clear();
        command.CommandText =
            "UPDATE `users` SET last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
        command.Parameters.AddWithValue("@id", connection.AccountId.Value);
        command.Parameters.AddWithValue("@last_ip", connection.LastIp.ToString());
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)connection.LastLogin).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)connection.LastLogin).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            logger.LogWarning("Database update failed, error occurred while updating account login IP and time");
        }

        # endregion
    }

    /// <summary>
    /// Eu Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    /// <param name="password"></param>
    public async Task Login(ILoginConnection connection, string username, ReadOnlyMemory<byte> password)
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
                await CreateAndLoginInvalid(connection, username, password, connect);
            }
            else
            {
                await connection.SendPacketAsync(new ACLoginDeniedPacket(2), CancellationToken.None);
            }

            return;
        }

        var expectedPassword = Convert.FromBase64String(reader.GetString("password"));
        if (!password.Span.SequenceEqual(expectedPassword))
        {
            await connection.SendPacketAsync(new ACLoginDeniedPacket(2), CancellationToken.None);
            return;
        }

        var banned = reader.GetBoolean("banned");
        if (banned)
        {
            var banReason = (byte)reader.GetUInt32("ban_reason");
            await connection.SendPacketAsync(new ACLoginDeniedPacket(banReason), CancellationToken.None);
            return;
        }

        connection.AccountId = new AccountId(reader.GetUInt32("id"));
        connection.AccountName = username;
        connection.LastLogin = DateTime.UtcNow;
        connection.LastIp = connection.Ip;

        logger.LogInformation("{AccountName} connected.", connection.AccountName);
        await connection.SendPacketAsync(new ACJoinResponsePacket(0, 6), CancellationToken.None);
        await connection.SendPacketAsync(new ACAuthResponsePacket(connection.AccountId, 6), CancellationToken.None);

        await reader.CloseAsync();

        #region update account

        command.Parameters.Clear();
        command.CommandText =
            "UPDATE `users` SET last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
        command.Parameters.AddWithValue("@id", connection.AccountId.Value);
        command.Parameters.AddWithValue("@last_ip", connection.LastIp.ToString());
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)connection.LastLogin).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)connection.LastLogin).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            logger.LogWarning("Database update failed, error occurred while updating account login IP and time");
        }

        # endregion
    }

    public async Task CreateAndLoginInvalid(ILoginConnection connection, string username, ReadOnlyMemory<byte> password,
        MySqlConnection connect)
    {
        var pass = Convert.ToBase64String(password.Span);

        await using var command = connect.CreateCommand();
        command.CommandText =
            "INSERT into users (username, password, email, last_ip, last_login, created_at, updated_at) VALUES (@username, @password, @email, @last_ip, @last_login, @created_at, @updated_at)";
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@password", pass);
        command.Parameters.AddWithValue("@email", "");
        command.Parameters.AddWithValue("@last_ip", connection.Ip.ToString());
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@created_at", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            await connection.SendPacketAsync(new ACLoginDeniedPacket(2), CancellationToken.None);
            return;
        }

        logger.LogDebug("Created account from invalid username login with value {Username}", username);
        await Login(connection, username, password);
    }

    public void AddReconnectionToken(InternalConnection connection, GameServerId gsId, AccountId accountId, uint token)
    {
        var tokensForGameServer = _tokens.GetOrAdd(gsId, static _ => []);
        tokensForGameServer.TryAdd(token, accountId);
        connection.SendPacket(new LGPlayerReconnectPacket(token));
    }

    public async Task Reconnect(ILoginConnection connection, GameServerId gsId, AccountId accountId, uint token)
    {
        if (!_tokens.ContainsKey(gsId))
        {
            if (gameController.TryGetParentId(gsId, out var parentId))
                gsId = parentId;
            else
            {
                // TODO ...
                return;
            }
        }

        if (!_tokens[gsId].TryGetValue(token, out var value))
        {
            // TODO ...
            return;
        }

        if (value == accountId)
        {
            connection.AccountId = accountId;
            await connection.SendPacketAsync(new ACJoinResponsePacket(0, 6), CancellationToken.None);
            await connection.SendPacketAsync(new ACAuthResponsePacket(connection.AccountId, 6), CancellationToken.None);
        }
        else
        {
            // TODO ...
        }
    }
}
