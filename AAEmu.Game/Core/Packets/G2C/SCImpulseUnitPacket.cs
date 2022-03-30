using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCImpulseUnitPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly SkillCaster _skillCaster;
        private readonly Point _vel;
        private readonly Point _angvel;
        private readonly Point _impulse;
        private readonly Point _angimpulse;

        public SCImpulseUnitPacket(uint objId, SkillCaster skillCaster, Point vel, Point angvel, Point impulse, Point angimpulse) : base(SCOffsets.SCImpulseUnitPacket, 5)
        {
            _objId = objId;
            _skillCaster = skillCaster;
            _vel = vel;
            _angvel = angvel;
            _impulse = impulse;
            _angimpulse = angimpulse;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);       // targetUnitId
            stream.Write(_skillCaster);   // skillCaster
            
            stream.Write(_vel.X);         // Position vel
            stream.Write(_vel.Y);
            stream.Write(_vel.Z);

            stream.Write(_angvel.X);      // Position angel
            stream.Write(_angvel.Y);
            stream.Write(_angvel.Z);

            stream.Write(_impulse.X);     // Position impulse
            stream.Write(_impulse.Y);
            stream.Write(_impulse.Z);

            stream.Write(_angimpulse.X);  // Position angimpulse
            stream.Write(_angimpulse.Y);
            stream.Write(_angimpulse.Z);

            return stream;
        }
    }
}
