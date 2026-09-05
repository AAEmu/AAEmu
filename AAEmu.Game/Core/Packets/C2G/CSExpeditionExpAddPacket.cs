using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client only ever sends this once per day as a "new day, refresh" heartbeat with Addexp
/// hardcoded to 0, not a genuine EXP submission. The client-supplied amount must never be trusted
/// directly into guild EXP - any guild member could otherwise submit an arbitrary amount and
/// instantly level their guild. Discarded entirely below rather than guessed at.
/// </summary>
public class CSExpeditionExpAddPacket() : GamePacket(CSOffsets.CSExpeditionExpAddPacket, 1)
{
    public int TypeValue { get; private set; }
    public uint Addexp { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Addexp = stream.ReadUInt32();
        // Intentionally discarded - see class summary.
    }
}
