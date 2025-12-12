using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Mission;

namespace AAEmu.Game.Models.CryEngine.Readers;

public class FlightMissionReader(System.IO.Stream rawStream, uint zoneId) : BaiReader(rawStream, zoneId)
{
    public static int BaiFlightNavFileVersionRead = 9;

    public FlightNavRegion FlightNavRegion { get; set; }

    //

    public override void CheckVersion(int version)
    {
        if (version > BaiFlightNavFileVersionRead)
        {
            throw new GameException("Wrong flight BAI file version - found " + version + ", expected " + BaiFlightNavFileVersionRead);
        }
    }

    protected override void ReadFromFile()
    {
        FlightNavRegion = new FlightNavRegion(ZoneId);
        if (Reader == null)
            return;

        FlightNavRegion.Name = "flight_nav_region";
        FlightNavRegion.HeightFieldOriginX = Reader.ReadInt32();
        FlightNavRegion.HeightFieldOriginY = Reader.ReadInt32();
        FlightNavRegion.HeightFieldDimX = Reader.ReadInt32();
        FlightNavRegion.HeightFieldDimY = Reader.ReadInt32();
        FlightNavRegion.ChildSubDiv = Reader.ReadInt32();
        FlightNavRegion.TerrainDownSample = Reader.ReadInt32();
        var spanCount = Reader.ReadUInt32();
        ReadSpans(FlightNavRegion, spanCount);
        var linkCount = Reader.ReadUInt32();
        ReadLinksDescriptors(FlightNavRegion, linkCount);
    }

    private void ReadSpans(FlightNavRegion flightNavRegion, uint spanCount)
    {
        flightNavRegion.SpanList.Clear();
        if (Reader == null)
            return;

        for (var i = 0; i < spanCount; i++)
        {
            var span = new Spans
            {
                X = Reader.ReadSingle() + ReaderPointOffset.X,
                Y = Reader.ReadSingle() + ReaderPointOffset.Y,
                MinZ = Reader.ReadSingle(),
                MaxZ = Reader.ReadSingle(),
                MaxRadius = Reader.ReadSingle(),
                Classification = Reader.ReadInt32(),
                ChildIdx = Reader.ReadInt32(),
                NextIdx = Reader.ReadInt32()
            };
            flightNavRegion.SpanList.Add(span);
        }
    }

    private void ReadLinksDescriptors(FlightNavRegion flightNavRegion, uint linkCount)
    {
        flightNavRegion.FlightLinkDescriptorList.Clear();
        if (Reader == null)
            return;

        for (var i = 0; i < linkCount; i++)
        {
            var flightLinkDescriptor = new FlightLinkDescriptor
            {
                IndexFirst = Reader.ReadInt32(), IndexSecond = Reader.ReadInt32()
            };
            flightNavRegion.FlightLinkDescriptorList.Add(flightLinkDescriptor);
        }
    }
}
