using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSApplyToInstantGamePacket() : GamePacket(CSOffsets.CSApplyToInstantGamePacket, 1)
{
    private uint _instanceId;
    private InstantCorps _corps;

    public override void Read(PacketStream stream)
    {
        _instanceId = stream.ReadUInt32();
        _corps = (InstantCorps)stream.ReadByte();

        InstantGameManager.Instance.ApplyToBattlefield(_instanceId, _corps, Connection.ActiveChar);
    }
}
