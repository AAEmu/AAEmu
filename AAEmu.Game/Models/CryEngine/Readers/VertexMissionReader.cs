using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Readers;

public class VertexMissionReader(System.IO.Stream rawStream, uint zoneId) : BaiReader(rawStream, zoneId)
{
    public static int BaiVertexFileVersion = 2;

    public List<ObstacleDataDescriptor> ObstacleDataDescriptorList { get; set; } = [];

    //

    public override void CheckVersion(int version)
    {
        if (version > BaiVertexFileVersion)
        {
            throw new GameException("Wrong vertex list BAI file version " + version + " expected " + BaiVertexFileVersion);
        }
    }

    protected override void ReadFromFile()
    {
        if (Reader == null)
            return;

        var count = Reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var obstacleDataDescriptor = new ObstacleDataDescriptor(ZoneId);
            obstacleDataDescriptor.Pos = ReadVector3();
            obstacleDataDescriptor.Dir = ReadVector3(true);
            obstacleDataDescriptor.ApproxRadius = Reader.ReadSingle();
            obstacleDataDescriptor.Flags = Reader.ReadByte();
            obstacleDataDescriptor.ApproxHeight = Reader.ReadByte();
            obstacleDataDescriptor.Unk1 = Reader.ReadByte();
            obstacleDataDescriptor.Unk2 = Reader.ReadByte();

            ObstacleDataDescriptorList.Add(obstacleDataDescriptor);
        }
    }
}
