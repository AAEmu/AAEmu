using System.Numerics;

namespace AAEmu.Game.Models.ClientData
{
    public class AABB
    {
        public Vector3 Min;
        public Vector3 Max;

        public AABB()
        {
            Min = new Vector3();
            Max = new Vector3();
        }
    }
}
