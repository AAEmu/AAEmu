using AAEmu.Commons.Network;
using AAEmu.World.Core.Packets.Wz;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZJoinResponse (op 0x000) — sequential ISerialize body from
/// reason(u16), fset(bytes, max 31), bf(u8), type(u16), pvp(u8), duel(u8),
/// type(u16), peaceMin(u32), filterBufferSize(u32), filterBuffer(bytes).
/// </summary>
/// <remarks>
/// immediately spends two of its bits: <see cref="ZoneFeature.PhysicsDamage"/> arms the collision
/// manager (logged by the dedicate as "activate physics damage"), and
/// <see cref="ZoneFeature.ServerSideShipSim"/> switches ship simulation to the server. With those
/// clear a dedicate silently reports no contacts at all, so hulls can never take collision damage.
/// </remarks>
public class WZJoinResponsePacket : ZonePacket
{
    /// <summary>
    /// Zone feature bits inside the fset blob. Numbering is byte-major:
    /// bit n lives in <c>fset[n / 8]</c> at <c>1 &lt;&lt; (n % 8)</c>, which is how the handler's
    /// </summary>
    public enum ZoneFeature
    {
        PhysicsDamage = 60,
        ServerSideShipSim = 177
    }

    /// <summary>Widest fset the dedicate will read; the handler consumes 32 bytes as 4 × u64.</summary>
    private const int FsetLength = 31;

    /// <summary>Both default on; set the env var to 0 to join a dedicate without that feature.</summary>
    private static readonly (ZoneFeature Feature, string KillSwitch)[] EnabledFeatures =
    [
        (ZoneFeature.PhysicsDamage, "AAEMU_WZ_FSET_PHYSICS_DAMAGE"),
        (ZoneFeature.ServerSideShipSim, "AAEMU_WZ_FSET_SERVER_SIM")
    ];

    private readonly byte[] _featureSet;

    public WZJoinResponsePacket(byte[] featureSet = null) : base(WzOpcodes.JoinResponse)
    {
        _featureSet = featureSet ?? BuildFeatureSet();
        if (_featureSet.Length is 0 or > FsetLength)
            throw new ArgumentOutOfRangeException(nameof(featureSet), "fset must be 1..31 bytes");
    }

    private static byte[] BuildFeatureSet()
    {
        var fset = new byte[FsetLength];

        // Every build so far joined with the single byte of the string "1". Its bits (0, 4, 5) have
        // never been identified, so they stay set rather than risk turning off something live.
        fset[0] = (byte)'1';

        foreach (var (feature, killSwitch) in EnabledFeatures)
        {
            if (Environment.GetEnvironmentVariable(killSwitch) == "0")
                continue;
            var bit = (int)feature;
            fset[bit / 8] |= (byte)(1 << (bit % 8));
        }

        return fset;
    }

    /// <summary>Feature bits set in this response, for the join log.</summary>
    public string DescribeFeatures()
    {
        var names = new List<string>();
        foreach (var (feature, _) in EnabledFeatures)
        {
            var bit = (int)feature;
            if ((_featureSet[bit / 8] & (1 << (bit % 8))) != 0)
                names.Add($"{feature}({bit})");
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    protected override void WriteBody(PacketStream stream)
    {
        stream.Write((ushort)0); // reason = accepted
        stream.Write(_featureSet, true); // u16 len + bytes, same framing as a string field
        stream.Write((byte)0); // bf
        stream.Write((ushort)0); // type
        stream.Write((byte)0); // pvp
        stream.Write((byte)0); // duel
        stream.Write((ushort)0); // type (second)
        stream.Write((uint)0); // peaceMin
        stream.Write((uint)0); // filterBufferSize (empty → all content)
        // filterBuffer omitted when size == 0
    }
}
