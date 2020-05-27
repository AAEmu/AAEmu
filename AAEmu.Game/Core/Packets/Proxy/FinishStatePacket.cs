using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FinishStatePacket : GamePacket
    {
        public FinishStatePacket() : base(0x001, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var state = stream.ReadInt32();

            switch (state)
            {
                case 0:
                    DbLoggerCategory.Database.Connection.SendPacket(new ChangeStatePacket(1));
                    // Connection.SendPacket(new SCHackGuardRetAddrsRequestPacket(false, false)); // HG_REQ? // TODO - config files
                    var levelname = string.Empty;
                    if (DbLoggerCategory.Database.Connection.ActiveChar != null)
                    {
                        levelname = ZoneManager.Instance.GetZoneByKey(DbLoggerCategory.Database.Connection.ActiveChar.Position.ZoneId).Name;
                    }
                    else
                    {
                        levelname = "w_hanuimaru_1";
                    }
                    DbLoggerCategory.Database.Connection.SendPacket(new SetGameTypePacket(levelname, 0, 1)); // TODO - level
                    DbLoggerCategory.Database.Connection.SendPacket(new SCInitialConfigPacket());
                    DbLoggerCategory.Database.Connection.SendPacket(new SCTrionConfigPacket(
                        true,
                        "https://session.draft.integration.triongames.priv",
                        "http://archeage.draft.integration.triongames.priv/commerce/pruchase/credits/purchase-credits-flow.action",
                        "")
                    ); // TODO - config files
                    DbLoggerCategory.Database.Connection.SendPacket(new SCAccountInfoPacket(
                            (int)DbLoggerCategory.Database.Connection.Payment.Method,
                            DbLoggerCategory.Database.Connection.Payment.Location,
                            DbLoggerCategory.Database.Connection.Payment.StartTime,
                            DbLoggerCategory.Database.Connection.Payment.EndTime)
                    );
                    DbLoggerCategory.Database.Connection.SendPacket(new SCChatSpamDelayPacket());
                    DbLoggerCategory.Database.Connection.SendPacket(new SCAccountAttributeConfigPacket(new[] { false, true })); // TODO
                    DbLoggerCategory.Database.Connection.SendPacket(new SCLevelRestrictionConfigPacket(10, 10, 10, 10, 10,
                        new byte[] { 0, 15, 15, 15, 0, 0, 15, 0, 0, 0, 0, 0, 0, 0, 15 })); // TODO - config files
                    break;
                case 1:
                    DbLoggerCategory.Database.Connection.SendPacket(new ChangeStatePacket(2));
                    break;
                case 2:
                    DbLoggerCategory.Database.Connection.SendPacket(new ChangeStatePacket(3));
                    break;
                case 3:
                case 4:
                case 5:
                case 6:
                    DbLoggerCategory.Database.Connection.SendPacket(new ChangeStatePacket(state + 1));
                    break;
                case 7:
                    break;
                default:
                    _log.Info("Unknown state: {0}", state);
                    break;
            }
        }
    }
}
