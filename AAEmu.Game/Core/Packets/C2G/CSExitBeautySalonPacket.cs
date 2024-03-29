//using AAEmu.Commons.Network;
//using AAEmu.Game.Core.Network.Game;
//using AAEmu.Game.Core.Packets.G2C;
//using AAEmu.Game.Models.Game.Skills;

//namespace AAEmu.Game.Core.Packets.C2G;

//public class CSExitBeautySalonPacket : GamePacket
//{
//    public CSExitBeautySalonPacket() : base(CSOffsets.CSExitBeautySalonPacket, 5)
//    {
//    }

//    public override void Read(PacketStream stream)
//    {
//        //Logger.Debug("ExitBeautySalon");
//        Connection.ActiveChar.SendPacket(new SCToggleBeautyshopResponsePacket(0));
//        Connection.ActiveChar.Buffs.RemoveBuff((uint)BuffConstants.InBeautySalon);
//    }
//}
