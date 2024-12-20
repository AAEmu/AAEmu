using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitLootingStatePacket : GamePacket
{
    private readonly uint _unitObjId;
    private readonly byte _looting;

    /// <summary>
    /// Sets the state of a unit if they are busy looting or not
    /// </summary>
    /// <param name="unitObjId"></param>
    /// <param name="looting">Looting state, 2 seems to be "done looting"</param>
    public SCUnitLootingStatePacket(uint unitObjId, byte looting) : base(SCOffsets.SCUnitLootingStatePacket, 1)
    {
        _unitObjId = unitObjId;
        _looting = looting;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_unitObjId);
        stream.Write(_looting);
        return stream;
    }
}
