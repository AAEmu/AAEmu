using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetForceAttackPacket : GamePacket
    {
        public CSSetForceAttackPacket() : base(0x04f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var on = stream.ReadBoolean();
            Connection.ActiveChar.SetForceAttack(on);
        }
    }
}
