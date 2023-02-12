using System.Collections.Generic;
using System.Drawing;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
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
                DuelManager.Instance.DuelCancel(challengerId, (ErrorMessageType)errorMessage);
                return;
            }

            DuelManager.Instance.DuelAccepted(Connection.ActiveChar, challengerId);
        }
    }
}
