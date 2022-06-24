using System.Collections.Generic;

namespace AAEmu.Game.Core.Network.Connections
{
    public interface IGameConnectionTable
    {
        void AddConnection(GameConnection con);
        GameConnection GetConnection(uint id);
        GameConnection GetConnectionByAccount(uint accountId);
        List<GameConnection> GetConnections();
        GameConnection RemoveConnection(uint id);
    }
}