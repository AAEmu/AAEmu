using System.Collections.Generic;
using System.Drawing;
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
        public CSStartDuelPacket() : base(CSOffsets.CSStartDuelPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var challengerId = stream.ReadUInt32();  // ID of the one who challenged us to a duel
            var errorMessage = stream.ReadInt16();  // 0 - accepted the duel, 507 - refused

            _log.Warn("StartDuel, Id: {0}, ErrorMessage: {1}", challengerId, errorMessage);

            if (errorMessage != 0)
            {
                return;
            }

            var challengedObjId = Connection.ActiveChar.ObjId;
            var challenger = WorldManager.Instance.GetCharacterById(challengerId);
            var challengerObjId = challenger.ObjId;

            Connection.ActiveChar.BroadcastPacket(new SCDuelStartedPacket(challengerObjId, challengedObjId), true);
            Connection.ActiveChar.BroadcastPacket(new SCAreaChatBubblePacket(true, Connection.ActiveChar.ObjId, 543), true);
            Connection.ActiveChar.BroadcastPacket(new SCDuelStartCountdownPacket(), true);

            var doodadFlag = new DoodadSpawner();
            const uint unitId = 5014u; // Combat Flag
            doodadFlag.Id = 0;
            doodadFlag.UnitId = unitId;
            doodadFlag.Transform = Connection.ActiveChar.Transform.CloneDetached();
            doodadFlag.Transform.Local.SetPosition(
                Connection.ActiveChar.Transform.World.Position.X - (Connection.ActiveChar.Transform.WorldPosition.X - challenger.Transform.WorldPosition.X) / 2,
                Connection.ActiveChar.Transform.WorldPosition.Y - (Connection.ActiveChar.Transform.WorldPosition.Y - challenger.Transform.WorldPosition.Y) / 2,
                Connection.ActiveChar.Transform.WorldPosition.Z - (Connection.ActiveChar.Transform.WorldPosition.Z - challenger.Transform.WorldPosition.Z) / 2
            );
            doodadFlag.Spawn(0);

            Connection.ActiveChar.BroadcastPacket(new SCDuelStatePacket(challengerObjId, doodadFlag.Last.ObjId), true);
            Connection.ActiveChar.BroadcastPacket(new SCDuelStatePacket(challengedObjId, doodadFlag.Last.ObjId), true);
            Connection.SendPacket(new SCDoodadPhaseChangedPacket(doodadFlag.Last));
            Connection.SendPacket(new SCCombatEngagedPacket(challengerObjId));
            Connection.ActiveChar.BroadcastPacket(new SCCombatEngagedPacket(challengedObjId), false);
        }
    }
}
