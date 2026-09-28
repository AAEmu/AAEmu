using System.IO;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeHouseNamePacket() : GamePacket(CSOffsets.CSChangeHouseNamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tl = stream.ReadUInt16(); // houseId
        var name = stream.ReadString();

        // ChangeHouseName capitalises the first character with name.Substring(0, 1), which throws on
        // the empty string a short read produces. That throw is caught by the dispatch, so the
        // rename does not land - but it is an accident, not a guard, and the log it produces reads
        // like a handler bug. Refuse the body here instead.
        if (stream.Overran)
            throw new InvalidDataException("ChangeHouseName: truncated body");

        Logger.Debug("ChangeHouseName, Tl: {0}, Name: {1}", tl, name);
        HousingManager.Instance.ChangeHouseName(Connection, tl, name);
    }
}
