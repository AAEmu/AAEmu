using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Models;

public class GameServer(GameServerId id, string name, string host, ushort port)
{
    public GameServerId Id { get; } = id;
    public string Name { get; } = name;
    public string Host { get; } = host;
    public ushort Port { get; } = port;
    public InternalConnection? Connection { get; set; }
    public bool Active => Connection != null;
    public GSLoad Load { get; set; }
    public List<GameServerId> MirrorsId { get; } = [];

    public void SendPacket(InternalPacket packet)
    {
        Connection?.SendPacket(packet);
    }
}
