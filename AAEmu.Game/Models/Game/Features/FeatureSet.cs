using System;
using System.Text;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Features;

/// <summary>
/// The <c>fset</c> blob sent to the client in SCInitialConfigPacket (opcode 0x007), version 10.0.2.13.
/// </summary>
/// <remarks>
/// <para>
/// This is client advertisement, not server enablement. The blob is serialized into
/// SCInitialConfig and read nowhere else, so a <see cref="Feature"/> bit decides what the client
/// shows and what packets it is willing to send - it does not gate the handler behind that packet,
/// nor any server-side progression. Clearing a bit hides the feature in the client while the
/// server logic stays live for anything that sends the packet regardless. Two kinds of exception
/// exist: the server-only switches in the region below, which left the blob in 10.0.2.13 and are
/// read directly by server code, and <see cref="Core.Managers.FeaturesManager.HeirEnabled"/>,
/// where the heir bits deliberately gate the server too because heir progression leaves state
/// behind whether or not the client can see it.
/// </para>
/// <para>
/// Wire layout is a fixed 31-byte bitmap. The client reads it with
/// <c>ReadString("fset", buf, 31)</c> (u16 length prefix + N bytes, <c>0 &lt; N &lt;= 31</c>) and copies
/// it into its game context at +40. A shorter blob is accepted silently, leaving the tail bytes
/// zeroed - which is exactly how the old 11-byte 1.2 blob failed quietly on a 10.0.2.13 client.
/// </para>
/// <para>
/// Four bytes are numeric scalars rather than flags: 1, 8, 10 and 26. Setting a
/// <see cref="Feature"/> bit inside them would corrupt the number, so <see cref="Feature"/>
/// deliberately defines nothing there and <see cref="Set"/> rejects those indices.
/// </para>
/// </remarks>
public class FeatureSet
{
    public const int FsetLength = 31;

    private const int PlayerLevelLimitIndex = 1;
    private const int MateLevelLimitIndex = 8;
    private const int UnknownTimeLimitIndex = 10;
    private const int ButlerLevelLimitIndex = 26;

    /// <summary>Byte indices that hold a number instead of eight flags.</summary>
    private static readonly bool[] ScalarByte = BuildScalarMap();

    private readonly byte[] _fset = new byte[FsetLength];

    private static bool[] BuildScalarMap()
    {
        var map = new bool[FsetLength];
        map[PlayerLevelLimitIndex] = true;
        map[MateLevelLimitIndex] = true;
        map[UnknownTimeLimitIndex] = true;
        map[ButlerLevelLimitIndex] = true;
        return map;
    }

    private static (int byteIndex, int bitIndex) GetIndexes(Feature feature)
    {
        var value = (int)feature;
        return (value / 8, value % 8);
    }

    /// <summary>
    /// True when <paramref name="feature"/> is a bit this build can actually address.
    /// </summary>
    public static bool IsValid(Feature feature)
    {
        var value = (int)feature;
        if (value < 0 || value >= FsetLength * 8)
            return false;
        return !ScalarByte[value / 8];
    }

    public bool Check(Feature feature)
    {
        if (!IsValid(feature))
            throw new ArgumentException(
                $"Feature {(int)feature} is outside the 10.0.2.13 fset, or lands in a scalar byte.",
                nameof(feature));

        var (byteIndex, bitIndex) = GetIndexes(feature);
        return (_fset[byteIndex] & (1 << bitIndex)) != 0;
    }

    /// <summary>
    /// Sets or clears a feature bit. Returns false for out-of-range values and for bits that
    /// would land inside a scalar byte, instead of corrupting the blob.
    /// </summary>
    public bool Set(Feature feature, bool enabled)
    {
        if (!IsValid(feature))
            return false;

        var (byteIndex, bitIndex) = GetIndexes(feature);
        if (enabled)
            _fset[byteIndex] |= (byte)(1 << bitIndex);
        else
            _fset[byteIndex] &= (byte)~(1 << bitIndex);
        return true;
    }

    #region Scalar bytes

    /// <summary>fset[1] - level cap before ancestral levels.</summary>
    public byte PlayerLevelLimit
    {
        get => _fset[PlayerLevelLimitIndex];
        set => _fset[PlayerLevelLimitIndex] = value;
    }

    /// <summary>fset[8] - level cap for mounts and pets.</summary>
    public byte MateLevelLimit
    {
        get => _fset[MateLevelLimitIndex];
        set => _fset[MateLevelLimitIndex] = value;
    }

    /// <summary>fset[26] - level cap for butlers.</summary>
    public byte ButlerLevelLimit
    {
        get => _fset[ButlerLevelLimitIndex];
        set => _fset[ButlerLevelLimitIndex] = value;
    }

    /// <summary>
    /// Consumers sit in the trade / block_trade_by_nft cluster; the unit is not established yet.
    /// </summary>
    public byte UnknownTimeLimit
    {
        get => _fset[UnknownTimeLimitIndex];
        set => _fset[UnknownTimeLimitIndex] = value;
    }

    #endregion

    #region Server-only switches

    // 10.0.2.13 dropped these bits from the blob, but the server logic behind them is still
    // meaningful and never needed the client to know about it. They are plain server settings now,
    // set from Configurations/Features.json. Defaults match what the old 1.2 blob had enabled.

    /// <summary>
    /// Pay house tax with tax certificates instead of gold. Was 1.2 fset bit 59 (taxItem).
    /// Set from <c>Features.TaxItem</c>; read by HousingManager and MailManager.
    /// </summary>
    public bool TaxItem { get; set; } = true;

    /// <summary>
    /// Split specialty pack profit with the crafter. Was 1.2 fset bit 56 (backpackProfitShare).
    /// Set from <c>Features.BackpackProfitShare</c>; read by SpecialtyManager.
    /// </summary>
    public bool BackpackProfitShare { get; set; } = true;

    #endregion

    public override string ToString()
    {
        var hex = new StringBuilder(_fset.Length * 3);
        foreach (var b in _fset)
            hex.AppendFormat("{0:x2} ", b);
        return hex.ToString().TrimEnd();
    }

    public void Write(PacketStream stream)
    {
        stream.Write(_fset, true);
    }
}
