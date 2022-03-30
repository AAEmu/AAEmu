using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartDuelPacket : GamePacket
    {
        public CSStartDuelPacket() : base(CSOffsets.CSStartDuelPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var errorMessage = stream.ReadInt16(); // 0 - принял дуэль, 507 - отказался

            _log.Warn("StartDuel, Id: {0}, ErrorMessage: {1}", id, errorMessage);


            var challengerObjId = Connection.ActiveChar.ObjId;
            var challenged = WorldManager.Instance.GetCharacterById(id);
            var challengedObjId = challenged.ObjId;

            //Connection.ActiveChar.BroadcastPacket(new SCDuelStartedPacket(challengerObjId, challengedObjId), true);
            Connection.SendPacket(new SCDuelStartedPacket(challengerObjId, challengedObjId));

            Connection.SendPacket(new SCAreaChatBubblePacket(true, Connection.ActiveChar.ObjId, 543));

            Connection.SendPacket(new SCDuelStartCountdownPacket());
            //Connection.ActiveChar.BroadcastPacket(new SCDuelStartCountdownPacket(), true);



            var doodadFlag = DoodadManager.Instance.Create(0, 5014);

            Connection.SendPacket(new SCDoodadCreatedPacket(doodadFlag));

            //Connection.ActiveChar.BroadcastPacket(new SCDoodadCreatedPacket(flagObjId), true);

            //Connection.ActiveChar.BroadcastPacket(new SCDuelStatePacket(challengerObjId, flagObjId), true);
            //Connection.ActiveChar.BroadcastPacket(new SCDuelStatePacket(challengerObjId, flagObjId), true);
        }
    }
}
