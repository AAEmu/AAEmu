using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSaveDoodadUccStringPacket : GamePacket
{
    public CSSaveDoodadUccStringPacket() : base(CSOffsets.CSSaveDoodadUccStringPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var data = stream.ReadString();

        Logger.Warn("SaveDoodadUccString, ObjId: {0}", objId);
    }
}
