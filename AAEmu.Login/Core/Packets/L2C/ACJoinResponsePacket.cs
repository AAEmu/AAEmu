using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

public enum JoinResponseReason : ushort
{
    Success = 0,
    ProtocolMismatch = 1,
    ModeMismatch = 2
}

/// <summary>
/// An AFS (Account Feature Settings?) value.
/// </summary>
/// <param name="MaxCharactersPerAccount">The maximum number of characters per account.</param>
/// <param name="AdditionalCharactersPerServer">The additional number of characters per server when using the slot increase item.</param>
/// <param name="IsPreSelectCharacterPeriod">Whether the server is in character pre-creation mode.</param>
public readonly record struct AfsValue(
    byte MaxCharactersPerAccount,
    uint AdditionalCharactersPerServer,
    bool IsPreSelectCharacterPeriod)
{
    public static AfsValue FromULong(ulong afs)
    {
        var maxCharactersPerAccount = (byte)(afs & 0xFF);
        var isPreSelectCharacterPeriod = (afs & 0x100) != 0;
        var additionalSlotsPerServer = (uint)(afs >> 32);

        return new AfsValue(maxCharactersPerAccount, additionalSlotsPerServer, isPreSelectCharacterPeriod);
    }

    public ulong ToULong()
    {
        var afs = ((ulong)AdditionalCharactersPerServer << 32)
                  | (IsPreSelectCharacterPeriod ? 1UL << 8 : 0UL)
                  | MaxCharactersPerAccount;
        return afs;
    }
}

/// <summary>
/// A packet sent by the login server to the client in response to a successful authentication request.
/// </summary>
/// <param name="reason"></param>
/// <param name="afs"></param>
public class ACJoinResponsePacket(ushort reason, ulong afs, byte authId = 0)
    : LoginPacket(LCOffsets.ACJoinResponsePacket)
{
    public ACJoinResponsePacket(JoinResponseReason reason, AfsValue afs) : this((ushort)reason, afs.ToULong())
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(authId);
        stream.Write(reason);
        stream.Write(afs);

        // afs[0] -> max number of characters per account
        // afs[1] -> additional number of characters per server when using the slot increase item
        // afs[2] -> 1 - character pre-creation mode 1-режим предварительного создания персонажей

        return stream;
    }
}
