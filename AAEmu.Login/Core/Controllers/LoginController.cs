﻿using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Login.Core.Controllers;

public class LoginController : Singleton<LoginController>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<byte, Dictionary<uint, ulong>> _tokens; // gsId, [token, accountId]
    private static bool _autoAccount = AppConfiguration.Instance.AutoAccount;

    protected LoginController()
    {
        _tokens = new Dictionary<byte, Dictionary<uint, ulong>>();
    }

    /// <summary>
    /// Kr Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    public static void Login(LoginConnection connection, string username)
    {
        using (var connect = MySQL.CreateConnection())
        {
            using (var command = connect.CreateCommand())
            {
                command.CommandText = "SELECT * FROM users where username=@username";
                command.Parameters.AddWithValue("@username", username);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        connection.SendPacket(new ACLoginDeniedPacket(2));
                        return;
                    }

                    // TODO ... validation password

                    connection.AccountId = reader.GetUInt64("id");
                    connection.AccountName = username;
                    connection.LastLogin = DateTime.UtcNow;
                    connection.LastIp = connection.Ip;

                    Logger.Info("{0} connected...", connection.AccountName);
                    connection.SendPacket(new ACJoinResponsePacket(1, 0x02020402, 0));
                    connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 0));
                }
            }
        }
    }

    /// <summary>
    /// Eu, MailRu Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    /// <param name="password"></param>
    public static void Login(LoginConnection connection, string username, IEnumerable<byte> password)
    {
        using (var connect = MySQL.CreateConnection())
        {
            using (var command = connect.CreateCommand())
            {
                command.CommandText = "SELECT * FROM users where username=@username";
                command.Parameters.AddWithValue("@username", username);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        if (_autoAccount)
                        {
                            reader.Close();
                            CreateAndLoginInvalid(connection, username, password, connect);
                        }
                        else
                        {
                            connection.SendPacket(new ACLoginDeniedPacket(2));
                        }

                        return;
                    }

                    var pass = Convert.FromBase64String(reader.GetString("password"));
                    if (!pass.SequenceEqual(password))
                    {
                        connection.SendPacket(new ACLoginDeniedPacket(2));
                        return;
                    }

                    connection.AccountId = reader.GetUInt64("id");
                    connection.AccountName = username;
                    connection.LastLogin = DateTime.UtcNow;
                    connection.LastIp = connection.Ip;

                    Logger.Info("{0} connected...", connection.AccountName);
                    connection.SendPacket(new ACJoinResponsePacket(1, 0x02020402, 0));
                    connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 0));
                }
            }
        }
    }

    public static void CreateAndLoginInvalid(LoginConnection connection, string username, IEnumerable<byte> password, MySqlConnection connect)
    {
        var pass = Convert.ToBase64String(password.ToArray());

        using (var command = connect.CreateCommand())
        {
            command.CommandText =
                "INSERT into users (username, password, email, last_ip) VALUES (@username, @password, \"\", \"\")";
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue("@password", pass);
            command.Prepare();

            if (command.ExecuteNonQuery() != 1)
            {
                connection.SendPacket(new ACLoginDeniedPacket(2));
                return;
            }

            Logger.Debug("Created account from invalid username login with value:" + username);
            Login(connection, username, password);
        }
    }

    public void AddReconnectionToken(InternalConnection connection, byte gsId, ulong accountId, uint token)
    {
        if (!_tokens.ContainsKey(gsId))
            _tokens.Add(gsId, new Dictionary<uint, ulong>());

        _tokens[gsId].Add(token, accountId);
        connection.SendPacket(new LGPlayerReconnectPacket(token));
    }

    public void Reconnect(LoginConnection connection, byte gsId, ulong accountId, uint token)
    {
        if (!_tokens.ContainsKey(gsId))
        {
            var parentId = GameController.Instance.GetParentId(gsId);
            if (parentId != null)
                gsId = (byte)parentId;
            else
            {
                // TODO ...
                return;
            }
        }

        if (!_tokens[gsId].ContainsKey(token))
        {
            // TODO ...
            return;
        }

        if (_tokens[gsId][token] == accountId)
        {
            connection.AccountId = accountId;
            Logger.Info("{0} reconnected...", connection.AccountName);
            connection.SendPacket(new ACJoinResponsePacket(1, 0x02020402, 0));
            connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 0));
        }
        else
        {
            // TODO ...
        }
    }
}
