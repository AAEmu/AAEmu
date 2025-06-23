using System.Collections.Concurrent;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using AAEmu.Login.Models.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login.Core.Controllers;

public class LoginController(
    IGameController gameController,
    IOptions<AppConfiguration> appConfig,
    IDbContextFactory<LoginDbContext> dbFactory,
    TimeProvider timeProvider) : ILoginController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly bool _autoAccount = appConfig.Value.AutoAccount;

    private readonly ConcurrentDictionary<GameServerId, ConcurrentDictionary<uint, AccountId>>
        _tokens = []; // gsId, [token, accountId]

    /// <summary>
    /// Kr Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    public void Login(LoginConnection connection, string username)
    {
        using var dbContext = dbFactory.CreateDbContext();
        var user = dbContext.Users
            .FirstOrDefault(u => u.Username == username);

        if (user == null)
        {
            connection.SendPacket(new ACLoginDeniedPacket(2));
            return;
        }

        // TODO ... validation password

        connection.AccountId = user.Id;
        connection.AccountName = user.Username;
        connection.LastLogin = timeProvider.GetUtcNow().UtcDateTime;
        connection.LastIp = connection.Ip;

        connection.SendPacket(new ACJoinResponsePacket(0, 6));
        connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));

        user.LastIp = connection.LastIp.ToString();
        user.LastLogin = connection.LastLogin;
        user.UpdatedAt = connection.LastLogin;

        try
        {
            dbContext.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            Logger.Warn(ex, "Database update failed, error occurred while updating account login IP and time");
        }
    }

    /// <summary>
    /// Eu Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    /// <param name="password"></param>
    public void Login(LoginConnection connection, string username, ReadOnlySpan<byte> password)
    {
        using var dbContext = dbFactory.CreateDbContext();
        var user = dbContext.Users
            .FirstOrDefault(u => u.Username == username);

        if (user == null)
        {
            if (_autoAccount)
            {
                user = CreateAndLoginInvalid(dbContext, connection, username, password);
                
                // Failed to create account
                if (user == null)
                {
                    return;
                }
            }
            else
            {
                connection.SendPacket(new ACLoginDeniedPacket(2));
                return;
            }
        }

        var expectedPassword = Convert.FromBase64String(user.Password);
        if (!password.SequenceEqual(expectedPassword))
        {
            connection.SendPacket(new ACLoginDeniedPacket(2));
            return;
        }

        if (user.Banned)
        {
            connection.SendPacket(new ACLoginDeniedPacket(user.BanReason));
            return;
        }

        connection.AccountId = user.Id;
        connection.AccountName = username;
        connection.LastLogin = timeProvider.GetUtcNow().UtcDateTime;
        connection.LastIp = connection.Ip;

        Logger.Info("{0} connected.", connection.AccountName);
        connection.SendPacket(new ACJoinResponsePacket(0, 6));
        connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));

        user.LastIp = connection.LastIp.ToString();
        user.LastLogin = connection.LastLogin;
        user.UpdatedAt = connection.LastLogin;

        try
        {
            dbContext.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            Logger.Warn(ex, "Database update failed, error occurred while updating account login IP and time");
        }
    }

    private User? CreateAndLoginInvalid(LoginDbContext dbContext, LoginConnection connection, string username,
        ReadOnlySpan<byte> password)
    {
        var pass = Convert.ToBase64String(password);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newUser = new User
        {
            Username = username,
            Password = pass,
            Email = "",
            LastIp = connection.Ip.ToString(),
            LastLogin = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Users.Add(newUser);

        try
        {
            dbContext.SaveChanges();
        }
        catch (DbUpdateException)
        {
            connection.SendPacket(new ACLoginDeniedPacket(2));
            return null;
        }

        Logger.Debug("Created account from invalid username login with value:" + username);
        return newUser;
    }

    public void AddReconnectionToken(InternalConnection connection, GameServerId gsId, AccountId accountId, uint token)
    {
        var tokensForGameServer = _tokens.GetOrAdd(gsId, static _ => []);
        tokensForGameServer.TryAdd(token, accountId);
        connection.SendPacket(new LGPlayerReconnectPacket(token));
    }

    public void Reconnect(LoginConnection connection, GameServerId gsId, AccountId accountId, uint token)
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
            connection.SendPacket(new ACJoinResponsePacket(0, 6));
            connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));
        }
        else
        {
            // TODO ...
        }
    }
}
