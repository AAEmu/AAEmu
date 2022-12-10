using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class ShipRequestMoveType : MoveType
    {
        public sbyte Throttle { get; set; }
        public sbyte Steering { get; set; }
        
        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            Throttle = stream.ReadSByte();
            Steering = stream.ReadSByte();

        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(Throttle);
            stream.Write(Steering);
            return stream;
        }
    }
}
