using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeSquadOpenTypePacket() : GamePacket(CSOffsets.CSChangeSquadOpenTypePacket, 1)
{
    public sbyte OpenType { get; private set; }

    public override void Read(PacketStream stream)
    {
        OpenType = stream.ReadSByte();
        SquadManager.Instance.ChangeOpenType(Connection.ActiveChar, (SquadOpenType)OpenType);
    }
}
