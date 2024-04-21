using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCChatBubblePacket : GamePacket
{
    private readonly uint _bc;
    private readonly byte _kind1;
    private readonly byte _kind2;
    private readonly uint _type;
    private readonly string _text;

    /// <summary>
    /// Shows a chat-bubble above a unit
    /// </summary>
    /// <param name="bc">ObjId</param>
    /// <param name="kind1">Bubble-Type: 1 Normal, 2 Think, 3 ???, 4 No Bubble (blue text), others is normal as well</param>
    /// <param name="kind2">What type to use: 1 use text, 2 use type</param>
    /// <param name="type">bubble Id</param>
    /// <param name="text"></param>
    public SCChatBubblePacket(uint bc, byte kind1, byte kind2, uint type, string text)
        : base(SCOffsets.SCChatBubblePacket, 5)
    {
        _bc = bc;
        _kind1 = kind1;
        _kind2 = kind2;
        _type = type;
        _text = text;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_bc);
        stream.Write(_kind1);
        stream.Write(_kind2);
        if (_kind2 != 1)
        {
            stream.Write(_type);
        }
        else
        {
            stream.Write(_text);
        }

        return stream;
    }
}

