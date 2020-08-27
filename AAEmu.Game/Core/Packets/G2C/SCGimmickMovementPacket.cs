using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmickMovementPacket : GamePacket
    {
        private readonly Gimmick _gimmick;

        public SCGimmickMovementPacket(Gimmick gimmick) : base(SCOffsets.SCGimmickMovementPacket, 1)
        {
            _gimmick = gimmick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_gimmick.TemplateId);
            stream.Write(_gimmick.Time);

            stream.Write(Helpers.ConvertLongX(_gimmick.Position.X));  // WorldPos
            stream.Write(Helpers.ConvertLongX(_gimmick.Position.Y));
            stream.Write(_gimmick.Position.Z);

            stream.Write(_gimmick.Rotation.X); // Quaternion Rotation
            stream.Write(_gimmick.Rotation.Y);
            stream.Write(_gimmick.Rotation.Z);
            stream.Write(_gimmick.Rotation.W);

            stream.Write(_gimmick.Vel.X);
            stream.Write(_gimmick.Vel.Y);
            stream.Write(_gimmick.Vel.Z);
            stream.Write(_gimmick.AngVel.X);
            stream.Write(_gimmick.AngVel.Y);
            stream.Write(_gimmick.AngVel.Z);
            stream.Write(_gimmick.Scale);

            return stream;
        }
    }
}
