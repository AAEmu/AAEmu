using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Readers;

internal class WaypointSurfaceNavigationReader : BaiReader
{
    public int BaiWayPoint3DSurfaceFileVersion = 1;

    public List<WaypointSurfaceNavigation> WaypointSurfaceNavigationList { get; set; } = new();

    public WaypointSurfaceNavigationReader(System.IO.Stream rawStream, uint zoneId) : base(rawStream, zoneId)
    {
        //
    }

    public override void CheckVersion(int version)
    {
        if (version > BaiWayPoint3DSurfaceFileVersion)
        {
            throw new GameException("Wrong AI 3D surface waypoint file version " + version + " expected " + BaiWayPoint3DSurfaceFileVersion);
        }
    }

    public override void ReadFromFile()
    {
        if (Reader == null)
            return;

        var nodes = Reader.ReadUInt32();
        for (var i = 0; i < nodes; i++)
        {
            var waypointSurfaceNavigation = new WaypointSurfaceNavigation(ZoneId);
            waypointSurfaceNavigation.LinkedVolumeRecords.AddRange(ReadLinkedVolumeRecords());
            waypointSurfaceNavigation.LinkedFlightRecords.AddRange(ReadLinkedVolumeRecords());
            WaypointSurfaceNavigationList.Add(waypointSurfaceNavigation);
        }
    }

    private List<LinkRecord> ReadLinkedVolumeRecords()
    {
        var res = new List<LinkRecord>();
        if (Reader == null)
            return res;
        var records = Reader.ReadUInt32();
        for (var i = 0; i < records; i++)
        {
            var linkRecord = new LinkRecord
            {
                NodeIndex = Reader.ReadInt32(), RadiusOut = Reader.ReadSingle(), RadiusIn = Reader.ReadSingle()
            };
            res.Add(linkRecord);
        }

        return res;
    }
}
