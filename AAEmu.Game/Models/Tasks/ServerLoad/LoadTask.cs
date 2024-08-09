using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2L;

namespace AAEmu.Game.Models.Tasks.ServerLoad;

public class LoadTask : Task
{
    public override void Execute()
    {
        var loadRate = (double)AccountManager.Instance.Count() /
                       (double)AppConfiguration.Instance.Network.NumConnections;
        byte load = loadRate switch
        {
            >= 0.6 => // HIGH
                2,
            >= 0.3 => // MIDDLE
                1,
            _ => 0 // LOW
        };
        LoginNetwork.Instance.GetConnection()?.SendPacket(new GLGameServerLoadPacket(load));
    }
}
