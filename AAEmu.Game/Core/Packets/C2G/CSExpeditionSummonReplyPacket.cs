using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// which passes each field name alongside the value:
/// bool result, string name
/// </remarks>
public class CSExpeditionSummonReplyPacket() : GamePacket(CSOffsets.CSExpeditionSummonReplyPacket, 1)
{
    public bool Result { get; private set; }
    public string Name { get; private set; }

    public override void Read(PacketStream stream)
    {
        Result = stream.ReadBoolean();
        Name = stream.ReadString();

        if (Connection.ActiveChar is { } character)
            ExpeditionActivityServices.Get().ReplyToSummon(character, Result, Name);
    }
}
