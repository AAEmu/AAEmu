using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers.UnitManagers;
using System.Collections.Generic;


namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRollDicePacket : GamePacket
    {
        public CSRollDicePacket() : base(CSOffsets.CSRollDicePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {

            var max = stream.ReadUInt32();            
            CharacterManager.Instance.PlayerRoll(Connection.ActiveChar, int.Parse(max.ToString()));          
           
        }
    }
}
