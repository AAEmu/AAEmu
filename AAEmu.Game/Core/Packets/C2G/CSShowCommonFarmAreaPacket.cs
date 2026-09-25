using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The farm-area request (CS 0x15E) is a documented leftover: its body shares a serializer with an
/// unrelated packet and this build has no 10.x client that sends it, so there is no real caller to
/// wire a response to.
/// </summary>
/// <remarks>
/// The body is a single s32 farm type, and the matching answer (SC 0x220) is <c>u32 type</c>,
/// <c>s32 count</c> followed by a variable-layout unit-state position record per entry. That record
/// is a client-internal structure whose byte layout is not reproducible from the server, so the
/// response is intentionally not synthesised here. Placement (CS 0x164) and removal (CS 0x163) are
/// the live farm request paths.
/// </remarks>
public class CSShowCommonFarmAreaPacket() : GamePacket(CSOffsets.CSShowCommonFarmAreaPacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
    }

    public override void Execute()
    {
        // Unreachable in 10.x: no client sender. Log and answer nothing rather than fabricate the
        // position-carrying response.
        Logger.Debug("ShowCommonFarmArea (type {0}) ignored: no 10.x sender for this request.", TypeValue);
    }
}
