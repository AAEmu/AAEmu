using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

public class ACJoinResponsePacket(ushort reason, ulong afs) : LoginPacket(LCOffsets.ACJoinResponsePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(reason);
        stream.Write(afs);

        // afs[0] -> max number of characters per account
        // afs[1] -> additional number of characters per server when using the slot increase item
        // afs[2] -> 1 - character pre-creation mode 1-режим предварительного создания персонажей

        return stream;
    }
}
