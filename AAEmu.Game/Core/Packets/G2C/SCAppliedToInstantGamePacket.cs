using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppliedToInstantGamePacket : GamePacket
{
    private readonly uint _battlefieldId;
    private readonly InstantCorps _corps;
    private readonly ushort _errorMessageId;

    public SCAppliedToInstantGamePacket(uint battlefieldId, InstantCorps corps, ushort errorMessageId = 0)
        : base(SCOffsets.SCAppliedToInstantGamePacket, 1)
    {
        _battlefieldId = battlefieldId;
        _corps = corps;
        _errorMessageId = errorMessageId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_battlefieldId);
        stream.Write((byte)_corps);
        stream.Write(_errorMessageId);
        return stream;
    }
}
