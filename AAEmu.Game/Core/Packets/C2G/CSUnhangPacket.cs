using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnhangPacket : GamePacket
    {
        public CSUnhangPacket() : base(CSOffsets.CSUnhangPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var unitObjId = stream.ReadBc();
            // var targetObjId = stream.ReadBc(); // Not used in 1.2
            var targetObjId = 0u;
            var reason = stream.ReadUInt32();
            // 0 climbed off from bottom
            // 2 climbed off on top
            // 7 jumped off

            _log.Trace($"Unhang, unitObjId: {unitObjId}, targetObjId: {targetObjId}, Reason: {reason}");
            // For 1.2 the targetObjId is not sent, so we will need to grab our saved value from Transform
            // Later this can also be used to verify if it's the correct object
            var character = WorldManager.Instance.GetBaseUnit(unitObjId);
            if (character != null)
            {
                targetObjId = character.Transform.StickyParent?.GameObject?.ObjId ?? 0;
                character.Transform.StickyParent = null;
            }

            Connection.ActiveChar.BroadcastPacket(new SCUnhungPacket(unitObjId, targetObjId, reason),false);
        }
    }
}
