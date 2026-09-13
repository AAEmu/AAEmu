using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// The current client writes a u32 count followed by that many names and caps the array at 50.
/// </remarks>
public class CSExpeditionSummonGetPacket() : GamePacket(CSOffsets.CSExpeditionSummonGetPacket, 1)
{
    private const uint MaximumNames = 50;

    public uint Count { get; private set; }
    public IReadOnlyList<string> Names { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        if (Count > MaximumNames)
            throw new InvalidDataException("Too many expedition summon recipients.");

        var names = new List<string>(checked((int)Count));
        for (var i = 0u; i < Count; i++)
            names.Add(stream.ReadString());
        Names = names;

        if (Connection.ActiveChar is { } character)
            ExpeditionActivityServices.Get().RequestSummons(character, Names);
    }
}
