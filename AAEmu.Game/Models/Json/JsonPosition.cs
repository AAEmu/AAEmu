using System.Numerics;

namespace AAEmu.Game.Models.Json
{
    public class JsonPosition
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Roll { get; set; }
        public float Pitch { get; set; }
        public float Yaw { get; set; }

        public Vector3 AsVector3()
        {
            return new Vector3(X, Y, Z);
        }
    }
}
