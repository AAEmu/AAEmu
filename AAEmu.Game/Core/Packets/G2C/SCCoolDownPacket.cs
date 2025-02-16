using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

#pragma warning disable IDE0052 // Remove unread private members

public class SCCooldownsPacket : GamePacket
{
    private Character _chr;
    //private uint _skillId;
    private int _skillCount;
    private int _tagCount;
    private int _chargeCount;

    public SCCooldownsPacket() : base(SCOffsets.SCCooldownsPacket, 5)
    {
        _skillCount = 0;
        _tagCount = 0;
    }
    public SCCooldownsPacket(Character chr) : base(SCOffsets.SCCooldownsPacket, 5)
    {
        _chr = chr;
        _skillCount = 0;
        _tagCount = 0;
        _chargeCount = 0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        //TODO заготовка для пакета

        stream.Write(_skillCount); // skillCount
        for (var i = 0; i < _skillCount; i++)
        {
            stream.Write(0u); // type(id)
        }
        stream.Write(_tagCount); // tagCount
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u); // type(id) //tagId
        }
        stream.Write(_chargeCount); // chargeCount
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u); // type(id) //tagId
        }

        return stream;
    }
}
