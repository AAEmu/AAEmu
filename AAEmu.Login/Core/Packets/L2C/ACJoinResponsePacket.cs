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
/// <param name="AdditionalData">Additional data for the AFS value.</param>
/// <param name="IsPreSelectCharacterPeriod">Whether the server is in character pre-creation mode.</param>
public readonly record struct AfsValue(byte MaxCharactersPerAccount, byte AdditionalCharactersPerServer, ushort AdditionalData, bool IsPreSelectCharacterPeriod)
{
    public static AfsValue FromULong(ulong afs)
    {
        var maxCharactersPerAccount = (byte)(afs & 0xFF);
        var additionalSlotsPerServer = (byte)((afs >> 8) & 0xFF);
        var additionalData = (ushort)((afs >> 16) & 0xFFFF);
        var isPreSelectCharacterPeriod = (afs & 0x10000) != 0;
        //var additionalSlotsPerServer = (uint)(afs >> 32);

        return new AfsValue(maxCharactersPerAccount, additionalSlotsPerServer, additionalData, isPreSelectCharacterPeriod);
    }

    public ulong ToULong()
    {
        var afs = (IsPreSelectCharacterPeriod ? 1UL << 32 : 0UL)
                  | ((ulong)AdditionalData << 16)
                  | ((ulong)AdditionalCharactersPerServer << 8)
                  | (ulong)MaxCharactersPerAccount;
        return afs;
    }
}

/// <summary>
/// A packet sent by the login server to the client in response to a successful authentication request.
/// </summary>
/// <param name="reason"></param>
/// <param name="afs"></param>
public class ACJoinResponsePacket(byte authId, ushort reason, ulong afs) : LoginPacket(LCOffsets.ACJoinResponsePacket)
{
    public ACJoinResponsePacket(byte authId, JoinResponseReason reason, AfsValue afs) : this((byte) authId, (ushort)reason, afs.ToULong())
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
