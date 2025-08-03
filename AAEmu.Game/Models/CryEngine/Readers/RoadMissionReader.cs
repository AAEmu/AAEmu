using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Readers;

public class RoadMissionReader : BaiReader
{
    public static int BaiRoadsFileVersion = 2;

    public List<Road> RoadList { get; set; } = new();

    public RoadMissionReader(System.IO.Stream rawStream, uint zoneId) : base(rawStream, zoneId)
    {
        //
    }

    public override void CheckVersion(int version)
    {
        if (version > BaiRoadsFileVersion)
        {
            throw new GameException("Wrong road BAI file version - found " + version + " expected " + BaiRoadsFileVersion);
        }
    }

    protected override void ReadFromFile()
    {
        if (Reader == null)
            return;

        var roads = Reader.ReadUInt32();
        for (var i = 0; i < roads; i++)
        {
            var road = ReadRoad();
            if (road != null)
                RoadList.Add(road);
        }
    }

    public Road ReadRoad()
    {
        if (Reader == null)
            return null;

        var road = new Road(ZoneId);
        road.RoadNodeList.Clear();
        road.Name = ReadName();
        var points = Reader.ReadUInt32();
        for (var i = 0; i < points; i++)
        {
            var roadNode = new RoadNode
            {
                Pos = ReadVector3(), Width = Reader.ReadSingle(), Offset = Reader.ReadSingle()
            };
            road.RoadNodeList.Add(roadNode);
        }

        return road;
    }
}
