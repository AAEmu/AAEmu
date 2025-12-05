using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInviteToInstantGamePacket : GamePacket
{
    private readonly ZoneInstanceId _zoneInstanceId;
    private readonly uint _rulesetId;
    private readonly InstantCorps _corps;
    private readonly ulong _qualifiedId;

    public SCInviteToInstantGamePacket(ZoneInstanceId zoneInstanceId, uint rulesetId, InstantCorps corps, ulong qualifiedId)
        : base(SCOffsets.SCInviteToInstantGamePacket, 1)
    {
        _zoneInstanceId = zoneInstanceId;
        _rulesetId = rulesetId;
        _corps = corps;
        _qualifiedId = qualifiedId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneInstanceId);
        stream.Write(_rulesetId);
        stream.Write((byte)_corps);
        stream.Write(_qualifiedId);
        return stream;
    }
}