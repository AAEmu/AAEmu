﻿using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Managers.World
{
    public class StreamManager : Singleton<StreamManager>
    {
        // Unused: private static Logger _log = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<uint, uint> _accounts;

        protected StreamManager()
        {
            _accounts = new Dictionary<uint, uint>();
        }

        public static void Load()
        {
            // TODO ...
        }

        public void AddToken(uint accountId, uint connectionId)
        {
            _accounts.Add(connectionId, accountId);
        }

        public void RemoveToken(uint token)
        {
            _accounts.Remove(token);
        }

        public void Login(StreamConnection connection, uint accountId, uint token)
        {
            if (_accounts.ContainsKey(token))
            {
                if (accountId == _accounts[token])
                {
                    var gCon = GameConnectionTable.Instance.GetConnection(token);
                    connection.GameConnection = gCon;
                    connection.SendPacket(new TCJoinResponsePacket(0));
                }
                else
                {
                    _accounts.Remove(token);
                    connection.SendPacket(new TCJoinResponsePacket(1));
                }
            }
            else
                connection.SendPacket(new TCJoinResponsePacket(1));
        }

        public static void RequestCell(StreamConnection connection, uint instanceId, int x, int y)
        {
            if (connection is not null)
            {
                var worldId = connection.GameConnection?.ActiveChar?.Transform?.WorldId ?? WorldManager.DefaultWorldId;
                // TODO: Handle requests for instances correctly ?
                var doodads = WorldManager.Instance.GetInCell<Doodad>(worldId, x, y).ToArray();
                var requestId = connection.GetNextRequestId(doodads);
                var count = Math.Min(doodads.Length, 30);
                var res = new Doodad[count];
                Array.Copy(doodads, 0, res, 0, count);
                connection.SendPacket(new TCDoodadStreamPacket(requestId, count, res));
            }
        }

        public static void ContinueCell(StreamConnection connection, int requestId, int next)
        {
            var doodads = connection.GetRequest(requestId);
            if (doodads == null)
                return;
            if (next >= doodads.Length)
                connection.RemoveRequest(requestId);
            var count = Math.Min(doodads.Length - next, 30);
            var res = new Doodad[count];
            Array.Copy(doodads, next > 0 ? next - 1 : 0, res, 0, count);
            next += count;
            connection.SendPacket(new TCDoodadStreamPacket(requestId, next, res));
        }
    }
}
