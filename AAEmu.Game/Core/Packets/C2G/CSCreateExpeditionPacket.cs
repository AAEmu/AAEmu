using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateExpeditionPacket() : GamePacket(CSOffsets.CSCreateExpeditionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();
        var type = stream.ReadInt32();

        Logger.Debug("CreateExpedition, name: {0}, type: {1}", name, type);
        ExpeditionManager.Instance.CreateExpedition(name, Connection);
    }
}
