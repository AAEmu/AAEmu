using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCImpulseUnitPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly SkillCaster _skillCaster;
        private readonly Vector3 _vel;
        private readonly Vector3 _angvel;
        private readonly Vector3 _impulse;
        private readonly Vector3 _angimpulse;

        public SCImpulseUnitPacket(uint objId, SkillCaster skillCaster, Vector3 vel, Vector3 angvel, Vector3 impulse, Vector3 angimpulse) : base(SCOffsets.SCImpulseUnitPacket, 5)
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
