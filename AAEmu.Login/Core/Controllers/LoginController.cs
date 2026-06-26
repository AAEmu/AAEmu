using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace AAEmu.Login.Core.Controllers;

public partial class LoginController(
    IGameController gameController,
    IPasswordService passwordService,
    IOptions<AppConfiguration> appConfig,
    IOptions<KoreaAuthOptions> koreaOptions,
    IMySqlConnectionFactory connectionFactory,
    ILogger<LoginController> logger) : ILoginController
{
    private readonly bool _autoAccount = appConfig.Value.AutoAccount;
    private readonly KoreaAuthOptions _koreaOptions = koreaOptions.Value;

    private readonly ConcurrentDictionary<GameServerId, ConcurrentDictionary<uint, AccountId>>
        _tokens = []; // gsId, [token, accountId]

    // Allows Unicode letters and digits (any script), plus _ . - @. No control characters or newlines.
    [GeneratedRegex(@"^[\p{L}\p{Nd}_.\-@]{1,32}$")]
    private static partial Regex UsernameRegex();

    /// <summary>
    /// Eu Method Auth
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password sent by the client, with its encoding kind.</param>
    /// <param name="ip">The client IP address for recording.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<LoginResult> Login(string username, Password password, IPAddress ip,
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

            return new LoginResult(false, default, LoginDeniedReason.BadAccount);
        }

        var storedPassword = reader.GetString("password");
        var storedKoreaChallengeHash = reader.IsDBNull(reader.GetOrdinal("korea_challenge_hash"))
            ? null
            : reader.GetString("korea_challenge_hash");

        var verificationResult = passwordService.VerifyPassword(storedPassword, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return new LoginResult(false, default, LoginDeniedReason.BadAccount);
        }

        var banned = reader.GetBoolean("banned");
        if (banned)
        {
            var banReason = (LoginDeniedReason)(byte)reader.GetUInt32("ban_reason");
            return new LoginResult(false, default, banReason);
        }

        var accountId = new AccountId(reader.GetUInt32("id"));
        var now = DateTime.UtcNow;

        logger.LogInformation("{Username} connected.", username.ReplaceLineEndings(" "));

        await reader.CloseAsync();

        #region update account

        // Determine what needs rehashing, which is only possible when we have a plaintext password
        var rehashPbkdf2 = verificationResult == PasswordVerificationResult.SuccessRehashNeeded
                           && password.Kind == PasswordKind.Plaintext;
        var koreaRehashNeeded = _koreaOptions.Enabled
                                && password.Kind == PasswordKind.Plaintext
                                && (storedKoreaChallengeHash == null
                                    || Sha256Crypt.ParseRounds(storedKoreaChallengeHash) != _koreaOptions.Rounds);

        command.Parameters.Clear();

        if (rehashPbkdf2 && koreaRehashNeeded)
        {
            command.CommandText =
                "UPDATE `users` SET password = @password, korea_challenge_hash = @koreaHash," +
                " last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
            command.Parameters.AddWithValue("@password", passwordService.HashForStorage(password));
            command.Parameters.AddWithValue("@koreaHash",
                KoreaChallengeCrypt.Compute(password.Value, rounds: _koreaOptions.Rounds));
        }
        else if (rehashPbkdf2)
        {
            command.CommandText =
                "UPDATE `users` SET password = @password," +
                " last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
            command.Parameters.AddWithValue("@password", passwordService.HashForStorage(password));
        }
        else if (koreaRehashNeeded)
        {
            command.CommandText =
                "UPDATE `users` SET korea_challenge_hash = @koreaHash," +
                " last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
            command.Parameters.AddWithValue("@koreaHash",
                KoreaChallengeCrypt.Compute(password.Value, rounds: _koreaOptions.Rounds));
        }
        else
        {
            command.CommandText =
                "UPDATE `users` SET last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
        }

        command.Parameters.AddWithValue("@id", accountId.Value);
        command.Parameters.AddWithValue("@last_ip", ip.ToString());
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)now).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)now).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            logger.LogWarning("Database update failed, error occurred while updating account login IP and time");
        }

        #endregion

        return new LoginResult(true, accountId, default);
    }

    /// <summary>
    /// Token-trusted login for web/launcher auth: authenticates by username without a password check,
    /// creating the account when missing. The launcher session token is trusted upstream.
    /// </summary>
    public async Task<LoginResult> LoginTrusted(string username, IPAddress ip,
        CancellationToken cancellationToken)
    {
        if (!UsernameRegex().IsMatch(username))
            return new LoginResult(false, default, LoginDeniedReason.BadAccount);

        await using var connect = connectionFactory.CreateConnection();

        // Look up an existing account (no password verification — token is trusted).
        await using (var select = connect.CreateCommand())
        {
            select.CommandText = "SELECT id, banned, ban_reason FROM users WHERE username = @username";
            select.Parameters.AddWithValue("@username", username);
            await using var reader = select.ExecuteReader();
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingId = new AccountId(reader.GetUInt32("id"));
                var isBanned = reader.GetBoolean("banned");
                var banReason = isBanned ? (LoginDeniedReason)(byte)reader.GetUInt32("ban_reason") : default;
                await reader.CloseAsync();

                if (isBanned)
                    return new LoginResult(false, default, banReason);

                await UpdateLastLoginAsync(connect, existingId, ip);
                logger.LogInformation("{Username} connected (web-auth).", username.ReplaceLineEndings(" "));
                return new LoginResult(true, existingId, default);
            }
        }

        // No account yet — create one (trusted; password is a throwaway, never used for web-auth).
        var placeholderPassword = passwordService.HashForStorage(
            Password.FromPlaintext(Guid.NewGuid().ToString("N")));
        var nowUnix = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();

        await using (var insert = connect.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO users (username, password, email, last_ip, last_login, created_at, updated_at)" +
                " VALUES (@username, @password, @email, @last_ip, @last_login, @created_at, @updated_at)";
            insert.Parameters.AddWithValue("@username", username);
            insert.Parameters.AddWithValue("@password", placeholderPassword);
            insert.Parameters.AddWithValue("@email", "");
            insert.Parameters.AddWithValue("@last_ip", ip.ToString());
            insert.Parameters.AddWithValue("@last_login", nowUnix);
            insert.Parameters.AddWithValue("@created_at", nowUnix);
            insert.Parameters.AddWithValue("@updated_at", nowUnix);

            if (await insert.ExecuteNonQueryAsync() != 1)
                return new LoginResult(false, default, LoginDeniedReason.LoginUnknown);

            var newId = new AccountId((uint)insert.LastInsertedId);
            logger.LogInformation("{Username} created and connected (web-auth).", username.ReplaceLineEndings(" "));
            return new LoginResult(true, newId, default);
        }
    }

    private static async Task UpdateLastLoginAsync(MySqlConnection connect, AccountId accountId, IPAddress ip)
    {
        await using var update = connect.CreateCommand();
        update.CommandText =
            "UPDATE `users` SET last_ip = @last_ip, last_login = @last_login, updated_at = @updated_at WHERE id = @id";
        var nowUnix = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        update.Parameters.AddWithValue("@id", accountId.Value);
        update.Parameters.AddWithValue("@last_ip", ip.ToString());
        update.Parameters.AddWithValue("@last_login", nowUnix);
        update.Parameters.AddWithValue("@updated_at", nowUnix);
        await update.ExecuteNonQueryAsync();
    }

    public async Task<KoreaAuthInfo?> GetKoreaAuthInfoAsync(string username, CancellationToken cancellationToken)
    {
        await using var connect = connectionFactory.CreateConnection();
        await using var command = connect.CreateCommand();
        command.CommandText =
            "SELECT id, korea_challenge_hash FROM users WHERE username = @username";
        command.Parameters.AddWithValue("@username", username);
        await using var reader = command.ExecuteReader();

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var accountId = new AccountId(reader.GetUInt32("id"));

        if (reader.IsDBNull(reader.GetOrdinal("korea_challenge_hash")))
            return null;

        var stored = reader.GetString("korea_challenge_hash");
        var rawHash = new byte[32];
        var (rounds, salt) = Sha256Crypt.Parse(stored, rawHash);
        return new KoreaAuthInfo(accountId, rawHash.AsMemory(), salt, rounds);
    }

    public async Task<(bool Banned, LoginDeniedReason BanReason)> CheckBanStatusAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        await using var connect = connectionFactory.CreateConnection();
        await using var command = connect.CreateCommand();
        command.CommandText = "SELECT banned, ban_reason FROM users WHERE id = @accountId";
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = command.ExecuteReader();

        if (!await reader.ReadAsync(cancellationToken))
            return (false, default);

        var banned = reader.GetBoolean("banned");
        var banReason = banned ? (LoginDeniedReason)(byte)reader.GetUInt32("ban_reason") : default;
        return (banned, banReason);
    }

    private async Task<LoginResult> CreateAndLoginInvalid(string username, Password password,
        IPAddress clientIp, MySqlConnection connection)
    {
        if (!UsernameRegex().IsMatch(username))
            return new LoginResult(false, default, LoginDeniedReason.BadAccount);

        var passwordHash = passwordService.HashForStorage(password);

        await using var command = connection.CreateCommand();

        if (_koreaOptions.Enabled && password.Kind == PasswordKind.Plaintext)
        {
            var koreaHash = KoreaChallengeCrypt.Compute(password.Value, rounds: _koreaOptions.Rounds);
            command.CommandText =
                "INSERT into users (username, password, korea_challenge_hash, email, last_ip, last_login, created_at, updated_at)" +
                " VALUES (@username, @password, @koreaHash, @email, @last_ip, @last_login, @created_at, @updated_at)";
            command.Parameters.AddWithValue("@koreaHash", koreaHash);
        }
        else
        {
            command.CommandText =
                "INSERT into users (username, password, email, last_ip, last_login, created_at, updated_at)" +
                " VALUES (@username, @password, @email, @last_ip, @last_login, @created_at, @updated_at)";
        }

        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@password", passwordHash);
        command.Parameters.AddWithValue("@email", "");
        command.Parameters.AddWithValue("@last_ip", clientIp.ToString());
        command.Parameters.AddWithValue("@last_login", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@created_at", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@updated_at", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            return new LoginResult(false, default, LoginDeniedReason.LoginUnknown);
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
