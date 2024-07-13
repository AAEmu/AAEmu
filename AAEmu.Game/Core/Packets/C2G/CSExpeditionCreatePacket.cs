using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionCreatePacket : GamePacket
{
    public CSExpeditionCreatePacket() : base(CSOffsets.CSExpeditionCreatePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();
        var id = stream.ReadUInt32(); // motherId

        Logger.Debug($"CreateExpedition, name: {name}, id: {id}");
        ExpeditionManager.Instance.CreateExpedition(name, Connection);
    }
}
