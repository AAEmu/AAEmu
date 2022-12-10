using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResurrectCharacterPacket : GamePacket
    {
        public CSResurrectCharacterPacket() : base(CSOffsets.CSResurrectCharacterPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var inPlace = stream.ReadBoolean();

            _log.Debug("ResurrectCharacter, InPlace: {0}", inPlace);

            var portal = PortalManager.Instance.GetClosestReturnPortal(Connection.ActiveChar);

            if (inPlace)
            {
                Connection.ActiveChar.Hp = (int)(Connection.ActiveChar.MaxHp * (Connection.ActiveChar.ResurrectHpPercent / 100.0f));
                Connection.ActiveChar.Mp = (int)(Connection.ActiveChar.MaxMp * (Connection.ActiveChar.ResurrectMpPercent / 100.0f));
                Connection.ActiveChar.ResurrectHpPercent = 1;
                Connection.ActiveChar.ResurrectMpPercent = 1;
            }
            else
            {
                Connection.ActiveChar.Hp = (int)(Connection.ActiveChar.MaxHp * 0.1);
                Connection.ActiveChar.Mp = (int)(Connection.ActiveChar.MaxMp * 0.1);
            }

            if (portal.X != 0)
            {
                Connection.ActiveChar.BroadcastPacket(
                    new SCCharacterResurrectedPacket(
                        Connection.ActiveChar.ObjId,
                        portal.X,
                        portal.Y,
                        portal.Z,
                        portal.ZRot
                    ),
                    true
                );
            }
            else
            {
                Connection.ActiveChar.BroadcastPacket(
                    new SCCharacterResurrectedPacket(
                        Connection.ActiveChar.ObjId,
                        Connection.ActiveChar.Transform.World.Position.X,
                        Connection.ActiveChar.Transform.World.Position.Y,
                        Connection.ActiveChar.Transform.World.Position.Z,
                        0
                    ),
                    true
                );
            }

            Connection.ActiveChar.BroadcastPacket(
                new SCUnitPointsPacket(
                    Connection.ActiveChar.ObjId,
                    Connection.ActiveChar.Hp,
                    Connection.ActiveChar.Mp
                ),
                true
            );
            Connection.ActiveChar.IsUnderWater = false;
            Connection.ActiveChar.StartRegen();
            Connection.ActiveChar.Breath = Connection.ActiveChar.LungCapacity;
        }
    }
}
