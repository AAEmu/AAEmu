using System.Collections.Generic;

namespace AAEmu.Game.Core.Network.Connections
{
    public interface IGameConnectionTable
    {
        void AddConnection(IGameConnection con);
        IGameConnection GetConnection(uint id);
        IGameConnection GetConnectionByAccount(uint accountId);
        List<IGameConnection> GetConnections();
        IGameConnection RemoveConnection(uint id);
    }
}
