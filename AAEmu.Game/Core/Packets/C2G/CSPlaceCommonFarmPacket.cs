using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The farm <b>shape</b> placement request (CS 0x164). <b>Parsed correctly, deliberately not acted
/// on.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>The parser is the part of this handler that is correct and is kept.</b> The body is
/// <c>u32 type</c>, a <b>signed</b> <c>s32 count</c>, then — only when <c>count &gt; 0</c> — a loop of
/// <c>count</c> 12-byte <c>vec3</c> points. The count is signed, so a negative or zero count carries
/// no points at all, and the whole loop must be consumed: reading a single point would leave the
/// remainder of the request unread and desynchronise the stream. That matches the three body rows
/// the raw <c>ir</c> records for this packet in both builds.
/// </para>
/// <para>
/// <b>Execute is a logged no-op.</b> This is one of the farm shape-editor commands, not a planting
/// request: no file in the 7,737-file client Lua extract sends <c>0x164</c>, and the raw <c>ir</c>
/// records no handler for it in either build.
/// </para>
/// <para>
/// An earlier version validated the request and could refuse it. That validation was unsound in a way
/// worth recording, because it <i>looked</i> protective: it compared the request's <b>point count</b>
/// — the number of vertices in a shape polygon — against the farm group's <b>crop capacity</b>. Those
/// are unrelated numbers, so the check could not have meant anything, and the rules have been removed
/// rather than retuned. Nothing on this path mutates game state, so a rule that cannot be shown to be
/// correct has no reason to exist here.
/// </para>
/// </remarks>
public class CSPlaceCommonFarmPacket() : GamePacket(CSOffsets.CSPlaceCommonFarmPacket, 1)
{
    /// <summary>
    /// Upper bound on points read from one request. It matches the wire's own clamp, so a
    /// well-behaved client never reaches it and a hostile count is bounded instead of looping.
    /// </summary>
    public const int MaxPointCount = 128;

    public uint TypeValue { get; private set; }
    public int SignedCount { get; private set; }
    public List<Vector3> Points { get; } = [];

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt32();
        SignedCount = stream.ReadInt32();

        Points.Clear();
        if (SignedCount <= 0)
            return;

        var toRead = Math.Min(SignedCount, MaxPointCount);
        for (var index = 0; index < toRead; index++)
        {
            Points.Add(new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle()));
        }

        if (SignedCount > MaxPointCount)
        {
            Logger.Warn("PlaceCommonFarm: count {0} exceeds the {1}-point bound; extra points ignored.",
                SignedCount, MaxPointCount);
        }
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        Logger.Debug("PlaceCommonFarm ({0}) from {1}: type={2} count={3} points={4} — parsed and ignored. "
                     + "This opcode is a farm-shape editor command with no 10.x client sender and no handler "
                     + "in the retail server; it is not a request to plant crops.",
            0x164, character?.Name ?? "<no character>", TypeValue, SignedCount, Points.Count);
    }
}
