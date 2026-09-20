using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Shared client packet for several small flags. Body is <c>u32 timeType</c>, <c>bool use</c>,
/// <c>bool saveDb</c>. Mobilization's "do not receive today" checkbox is
/// <see cref="InstantTimeKind.MobilizationOrderNotRecv"/> with <c>saveDb</c> set.
/// </summary>
public class CSInstantTimePacket() : GamePacket(CSOffsets.CSInstantTimePacket, 1)
{
    public uint TimeType { get; private set; }
    public bool Use { get; private set; }
    public bool SaveDb { get; private set; }

    public override void Read(PacketStream stream)
    {
        TimeType = stream.ReadUInt32();
        Use = stream.ReadBoolean();
        SaveDb = stream.ReadBoolean();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (TimeType == (uint)InstantTimeKind.MobilizationOrderNotRecv)
            HeroManager.Instance.SetMobilizationOrderNotRecv(character, Use, SaveDb);
    }
}
