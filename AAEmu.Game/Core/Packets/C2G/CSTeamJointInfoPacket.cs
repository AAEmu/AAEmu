using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Requests joint information for a named raid team; the world manager owns the authorization
/// and the pending handshake state.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// ulong type, sbyte mode, string name, sbyte worldId
/// </remarks>
public class CSTeamJointInfoPacket() : GamePacket(CSOffsets.CSTeamJointInfoPacket, 1)
{
    public ulong Type { get; private set; }
    public sbyte Mode { get; private set; }
    public string Name { get; private set; }
    public sbyte WorldId { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();
        Mode = stream.ReadSByte();
        Name = stream.ReadString();
        WorldId = stream.ReadSByte();

        if (Connection?.ActiveChar is { } character)
            TeamJointManager.Instance.RequestJointInfo(character.Id, Type, Mode, Name, WorldId);
    }
}
