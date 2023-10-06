using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBeautyshopDataPacket : GamePacket
{
    public CSBeautyshopDataPacket() : base(CSOffsets.CSBeautyshopDataPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        //_logger.Debug("BeautyshopData");
        var hair = stream.ReadUInt32(); // unknown value ? maybe bitmask of what was changed ?
        var model = new UnitCustomModelParams();
        model.Read(stream);
        CharacterManager.ApplyBeautySalon(Connection.ActiveChar, hair, model);
    }
}
