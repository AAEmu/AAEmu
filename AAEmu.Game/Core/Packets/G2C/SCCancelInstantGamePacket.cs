using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Cancel matching queue. Wire layout and branch rules: <see cref="InstantGameWireContract"/>.
/// </summary>
public class SCCancelInstantGamePacket : GamePacket
{
    private readonly ushort _errorMessage;
    private readonly byte _fromHomeland;

    private SCCancelInstantGamePacket(ushort errorMessage, byte fromHomeland)
        : base(InstantGameWireContract.OpcodeCancel, 1)
    {
        _errorMessage = errorMessage;
        _fromHomeland = fromHomeland;
    }

    /// <summary>Player left queue or server withdrew — clears Instance apply/queue UI.</summary>
    public static SCCancelInstantGamePacket ClearQueue() =>
        new(0, InstantGameWireContract.CancelBranchClearQueue);

    /// <summary>Queue ended with a client error message id (Instance error branch).</summary>
    public static SCCancelInstantGamePacket WithError(ushort errorMessageId) =>
        new(errorMessageId, InstantGameWireContract.CancelBranchErrorOnly);

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_errorMessage);
        stream.Write(_fromHomeland);
        return stream;
    }
}
