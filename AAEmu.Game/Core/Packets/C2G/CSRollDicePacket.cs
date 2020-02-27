using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers.UnitManagers;
using System.Collections.Generic;


namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRollDicePacket : GamePacket
    {
        public CSRollDicePacket() : base(0x0bd, 1)
        {
        }

        public override void Read(PacketStream stream)
        {

            var max = stream.ReadUInt32();
            int min = 1;            
            CharacterManager.Instance.PlayerRoll(Connection.ActiveChar, min, int.Parse(max.ToString()));          
           
        }
    }
}
