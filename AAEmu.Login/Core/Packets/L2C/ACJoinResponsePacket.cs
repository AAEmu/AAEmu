using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACJoinResponsePacket : LoginPacket
    {
        private readonly ushort _reason;
        private readonly ulong _afs;

        public ACJoinResponsePacket(ushort reason, ulong afs) : base(0x00)
        {
            _reason = reason;
            _afs = afs;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_afs);

            // afs[0] -> макс кол-во персонажей на аккаунте
            // afs[1] -> дополнительно кол-во персонажей на сервер при использовании предмета увеличения слота
            // afs[2] -> 1 - режим предварительного создания персонажей

            return stream;
        }
    }
}
