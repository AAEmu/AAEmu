using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInviteSquadMemberPacket() : GamePacket(CSOffsets.CSInviteSquadMemberPacket, 1)
{
    public string CharName { get; private set; } = "";
    public byte WorldId { get; private set; }
    public uint CatalogId { get; private set; }

    public override void Read(PacketStream stream)
    {
        CharName = stream.ReadString();
        WorldId = stream.ReadByte();
        CatalogId = SquadFieldTypeWire.Read(stream).InstanceId;
        SquadManager.Instance.Invite(Connection.ActiveChar, CharName, WorldId, CatalogId);
    }
}
