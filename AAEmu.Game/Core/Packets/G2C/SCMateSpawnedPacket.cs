using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateSpawnedPacket : GamePacket
    {
        private readonly Mount _mate;

        public SCMateSpawnedPacket(Mount mate) : base(SCOffsets.SCMateSpawnedPacket, 5)
        {
            _mate = mate;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mate.TlId);

            stream.Write(_mate.Id);
            stream.Write(_mate.ItemId);
            stream.Write(_mate.UserState);
            stream.Write(_mate.Exp);
            stream.Write(_mate.Mileage);
            stream.Write(_mate.SpawnDelayTime);

            // TODO - max 10 skills
            foreach (var skill in _mate.Skills)
            {
                stream.Write(skill);
            }

            for (var i = 0; i < 10 - _mate.Skills.Count; i++)
            {
                stream.Write(0);
            }

            return stream;
        }
    }
}
