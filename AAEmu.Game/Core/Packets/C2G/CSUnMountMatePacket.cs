using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnMountMatePacket : GamePacket
    {
        public CSUnMountMatePacket() : base(0x0a8, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();
            var ap = stream.ReadByte();     // AttachPoint
            var reason = stream.ReadByte(); // AttachUnitReason

            //_log.Warn("UnMountMate, TlId: {0}, AttachPoint: {1}, AttachUnitReason: {2}", tlId, ap, reason);
            MateManager.Instance.UnMountMate(Connection.ActiveChar, tlId, (AttachPoint)ap, (AttachUnitReason)reason);
        }
    }
}
