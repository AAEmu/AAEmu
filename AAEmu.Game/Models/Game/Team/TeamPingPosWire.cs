using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Team;

/// <summary>
/// 10.0.2.13 team map-ping body shared by CSSetPingPos and SCTeamPingPos.
/// Client field names: teamId, setPingType, flag, 6× quantized WorldPos (ix0…), pish/pisc ×6 instance ids, lineCount, lineCount× pos.
/// </summary>
public static class TeamPingPosWire
{
    public const int SlotCount = 6;

    public static void Write(
        PacketStream stream,
        uint teamId,
        byte setPingType,
        bool hasPing,
        WorldSpawnPosition position,
        uint instanceId)
    {
        stream.Write(teamId);
        stream.Write(setPingType);
        // Bit i set ⇒ slot i is live; we only use slot 0 for the current single-ping model.
        stream.Write((byte)(hasPing ? 1 : 0));

        for (var i = 0; i < SlotCount; i++)
        {
            if (hasPing && i == 0 && position != null)
                stream.WritePosition(position.X, position.Y, position.Z);
            else
                stream.WritePosition(0f, 0f, 0f);
        }

        var instanceIds = new uint[SlotCount];
        if (hasPing)
            instanceIds[0] = instanceId;
        stream.WritePisc(instanceIds);

        stream.Write((byte)0); // lineCount — no path overlay
    }

    public static (uint teamId, byte setPingType, bool hasPing, WorldSpawnPosition position, uint instanceId) Read(
        PacketStream stream)
    {
        var teamId = stream.ReadUInt32();
        var setPingType = stream.ReadByte();
        var flag = stream.ReadByte();

        var positions = new WorldSpawnPosition[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var (x, y, z) = stream.ReadPosition();
            positions[i] = new WorldSpawnPosition { X = x, Y = y, Z = z };
        }

        var instanceIds = stream.ReadPisc(SlotCount);
        var lineCount = stream.ReadByte();
        for (var i = 0; i < lineCount; i++)
            _ = stream.ReadPosition();

        var hasPing = (flag & 1) != 0 || setPingType != 0;
        var position = positions[0] ?? new WorldSpawnPosition();
        var instanceId = instanceIds.Length > 0 ? instanceIds[0] : 0u;
        return (teamId, setPingType, hasPing, position, instanceId);
    }
}
