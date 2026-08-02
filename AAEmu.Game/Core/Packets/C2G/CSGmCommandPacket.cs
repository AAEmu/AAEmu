using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Gm;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Real client GM console (X2Gm / gm_console.lua) → World.
/// Wire: bc unitId, u16 cmd, string params (CRLF Data blob).
/// </summary>
public class CSGmCommandPacket() : GamePacket(CSOffsets.CSGmCommandPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var unitId = stream.ReadBc();
        var cmd = stream.ReadUInt16();
        var parameters = stream.ReadString() ?? "";

        Logger.Info("CSGmCommand unit={0} cmd={1} params={2}", unitId, cmd, parameters);

        if (Connection.GetAttribute("gmFlag") == null)
        {
            Logger.Warn("CSGmCommand rejected — no gmFlag (account access_level < 100?)");
            Connection.ActiveChar?.SendPacket(new SCGmCommandPacket(unitId, (byte)cmd, 0, parameters, "insufficient authority"));
            return;
        }

        var me = Connection.ActiveChar;
        if (me == null)
            return;

        GmCommandDispatcher.Handle(me, unitId, cmd, parameters);
    }
}
