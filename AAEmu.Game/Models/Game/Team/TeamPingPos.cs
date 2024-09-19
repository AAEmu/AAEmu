using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Team;

public enum PingType : byte
{
    PingTypeInvalid = 0,
    PingTypePing = 1,
    PingTypeEnemy = 2,
    PingTypeAttack = 3,
    PingTypeLine = 4,
    PingTypeEraser = 5
};

public class TeamPingPos : PacketMarshaler
{
    public uint TeamId { get; set; }
    public PingType SetPingType { get; set; }
    public byte Flag { get; set; }
    public byte LineCount { get; set; }
    public WorldSpawnPosition[] Positions { get; set; }
    public WorldSpawnPosition[] LinePositions { get; set; }
    public uint[] Pisc { get; set; }

    public TeamPingPos()
    {
        Positions = new WorldSpawnPosition[6];
        for (var i = 0; i < 6; i++)
        {
            Positions[i] = new WorldSpawnPosition();
        }
        Pisc = new uint[6];
        LinePositions = new WorldSpawnPosition[20];
        for (var i = 0; i < 20; i++)
        {
            LinePositions[i] = new WorldSpawnPosition();
        }
    }

    public override void Read(PacketStream stream)
    {
        TeamId = stream.ReadUInt32();
        SetPingType = (PingType)stream.ReadByte();
        Flag = stream.ReadByte();

        for (var i = 0; i < 6; i++)
        {
            var (x, y, z) = stream.ReadPosition();
            Positions[i].X = x;
            Positions[i].Y = y;
            Positions[i].Z = z;
        }

        // --- begin pish
        var mTmps = stream.ReadPisc(4);
        // --- end pish
        Pisc[0] = (uint)mTmps[0];
        Pisc[1] = (uint)mTmps[1];
        Pisc[2] = (uint)mTmps[2];
        Pisc[3] = (uint)mTmps[3];

        // --- begin pish
        mTmps = stream.ReadPisc(2);
        // --- end pish
        Pisc[4] = (uint)mTmps[0];
        Pisc[5] = (uint)mTmps[1];

        LineCount = stream.ReadByte();
        if (LineCount > 0 && LineCount < 20)
        {
            for (var i = 0; i < LineCount; i++) // не больше 20 точек
            {
                var (x, y, z) = stream.ReadPosition();
                LinePositions[i].X = x;
                LinePositions[i].Y = y;
                LinePositions[i].Z = z;
            }
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(TeamId);
        stream.Write((byte)SetPingType);
        stream.Write(Flag);
        for (var i = 0; i < 6; i++)
        {
            stream.WritePosition(Positions[i].X,Positions[i].Y,Positions[i].Z);
        }
        stream.WritePisc(Pisc[0], Pisc[1], Pisc[2], Pisc[3]);
        stream.WritePisc(Pisc[4], Pisc[5]);

        stream.Write(LineCount);
        if (LineCount > 0)
        {
            for (var i = 0; i < LineCount; i++) // не больше 20 точек
            {
                stream.WritePosition(LinePositions[i].X,LinePositions[i].Y,LinePositions[i].Z);
            }
        }

        return stream;
    }
}
