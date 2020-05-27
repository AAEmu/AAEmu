using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;
using AAEmu.Login.Models;

namespace AAEmu.Login.Login
{
    public enum LoadTypes
    {
        Normal = 0,
        Load = 1,
        Queue = 2
    }
    
    public class GameServerInstance : IWritable
    {
        public GameServer GameServer { get; set; }

        public LoadTypes LoadType
        {
            get
            {
                return LoadTypes.Normal; // TODO : Compute this
            }
        }

        public bool Active
        {
            get
            {
                return true;
            }
        }

        public GameServerInstance(GameServer gs)
        {
            GameServer = gs;
        }

        public void Write(PacketStream stream)
        {
            stream.Write((byte)GameServer.Id);
            stream.Write(GameServer.Name);
            stream.Write(Active);

            if (Active)
            {
                stream.Write((byte)LoadType);
                // TODO : Write congestion
                for (var i = 0; i < 9; i++) // race
                {
                    stream.Write((byte)0);
                }
            }
        }
    }
}
