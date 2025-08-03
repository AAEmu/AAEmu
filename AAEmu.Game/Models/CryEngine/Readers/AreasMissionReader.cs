using System.Collections.Concurrent;
using System.Numerics;
using NLog;
using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Mission;
using AAEmuGeoData.Scripts.CryEngine.Mission;

namespace AAEmu.Game.Models.CryEngine.Readers;

public class AreasMissionReader : BaiReader
{
    private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();
    public static int BaiAreasFileVersion = 21;

    public List<AiShape> ForbiddenAreasList { get; } = new();
    public List<SpecialArea> NavigationModifiers { get; } = new();
    public List<AiShape> DesignerForbiddenAreasList { get; } = new();
    public List<AiShape> ForbiddenBoundariesList { get; } = new();
    public List<AiShape> ExtraLinkCostsList { get; } = new();
    public List<PolygonArea> DesignerPathsList { get; } = new();
    public List<Vector3> Points { get; } = new();

    // Helpers to remove duplicates
    public static ConcurrentBag<string> UsedAreaNames { get; set; } = new();

    public AreasMissionReader(System.IO.Stream rawStream, uint zoneId) : base(rawStream, zoneId)
    {
        //
    }

    public override void CheckVersion(int version)
    {
        if (version > BaiAreasFileVersion)
            throw new GameException($"Wrong Areas BAI file version {version}, expected {BaiAreasFileVersion}");
    }

    protected override void ReadFromFile()
    {
        if (Reader == null)
            return;

        var numAreas = Reader.ReadUInt32();
        for (var i = 0; i < numAreas; i++)
        {
            var aiShape = new AiShape(ZoneId);
            ReadForbiddenArea(aiShape, ForbiddenAreasType.ForbiddenArea);
        }

        numAreas = Reader.ReadUInt32();
        for (var i = 0; i < numAreas; i++)
        {
            var specialArea = new SpecialArea(ZoneId);
            ReadNavigationModifiersArea(specialArea);
        }

        numAreas = Reader.ReadUInt32();
        for (var i = 0; i < numAreas; i++)
        {
            var aiShape = new AiShape(ZoneId);
            ReadForbiddenArea(aiShape, ForbiddenAreasType.DesignerForbiddenArea);
        }

        numAreas = Reader.ReadUInt32();
        for (var i = 0; i < numAreas; i++)
        {
            var aiShape = new AiShape(ZoneId);
            ReadForbiddenArea(aiShape, ForbiddenAreasType.ForbiddenBoundaries);
        }

        // If this is a file from paths, then it does not contain any ExtraLinkCost or
        // DesignerPath settings, and just ends here.
        if (ZoneId == 0)
        {
            if (Reader.BaseStream.Position != Reader.BaseStream.Length)
                Logger.Debug($"Reader ended before end of file at {Reader.BaseStream.Position}/{Reader.BaseStream.Length}");
            return;
        }

        numAreas = Reader.ReadUInt32();
        for (var i = 0; i < numAreas; i++)
        {
            var aiShape = new AiShape(ZoneId);
            ReadForbiddenArea(aiShape, ForbiddenAreasType.ExtraLink);
            ExtraLinkCostsList.Add(aiShape);
        }

        _ = Reader.ReadInt64(); // unk data

        numAreas = Reader.ReadUInt32();
        for (var i = 0; i < numAreas; i++)
        {
            var polygonArea = new PolygonArea(ZoneId);
            ReadDesignerPaths(polygonArea);
        }
    }

    private void ReadDesignerPaths(PolygonArea polygonArea)
    {
        if (Reader == null)
            return;

        polygonArea.Name = ReadName();

        polygonArea.Points.Clear();
        var pointsSize = Reader.ReadUInt32();
        for (var i = 0; i < pointsSize; i++)
            polygonArea.Points.Add(ReadVector3());

        polygonArea.NavigationType = (NavigationType)(1 << Reader.ReadInt32());
        polygonArea.Type = Reader.ReadInt32();
        polygonArea.Height = Reader.ReadSingle();
        polygonArea.AiLightLevel = (AiLightLevel)Reader.ReadInt32();

        if ((IgnoreDuplicateAreaNames == false) || !UsedAreaNames.Contains(polygonArea.Name))
        {
            DesignerPathsList.Add(polygonArea);
            UsedAreaNames.Add(polygonArea.Name);
        }
    }

    private void ReadNavigationModifiersArea(SpecialArea specialArea)
    {
        if (Reader == null)
            return;

        specialArea.Name = ReadName();
        specialArea.MissionType = (MissionType)Reader.ReadInt32();
        specialArea.WaypointConnections = (WaypointConnections)Reader.ReadInt32();
        specialArea.Altered = (Reader.ReadByte() != 0);

        _ = Reader.ReadSingle(); // junk?
        _ = Reader.ReadSingle(); // junk?

        specialArea.Height = Reader.ReadSingle();
        if (BaiAreasFileVersion <= 0x10)
        {
            _ = Reader.ReadSingle(); // junk?
        }

        specialArea.NodeAutoConnectDistance = Reader.ReadSingle();
        specialArea.MaxZ = Reader.ReadSingle();
        specialArea.MinZ = Reader.ReadSingle();
        specialArea.BuildingId = Reader.ReadInt32();
        if (BaiAreasFileVersion >= 0x12)
        {
            specialArea.AiLightLevel = (AiLightLevel)Reader.ReadByte();
        }

        ReadPoints(specialArea.Points);
        if ((IgnoreDuplicateAreaNames == false) || !UsedAreaNames.Contains(specialArea.Name))
        {
            NavigationModifiers.Add(specialArea);
            UsedAreaNames.Add(specialArea.Name);
        }
    }

    private void ReadForbiddenArea(AiShape aiShape, ForbiddenAreasType forbiddenArea)
    {
        if (Reader == null)
            return;

        aiShape.Name = ReadName();

        aiShape.Points.Clear();
        ;
        var pointsSize = Reader.ReadUInt32();
        for (int i = 0; i < pointsSize; i++)
        {
            aiShape.Points.Add(ReadVector3());
        }

        if (IgnoreDuplicateAreaNames && UsedAreaNames.Contains(aiShape.Name))
            return;
        UsedAreaNames.Add(aiShape.Name);

        switch (forbiddenArea)
        {
            case ForbiddenAreasType.ForbiddenArea:
                ForbiddenAreasList.Add(aiShape);
                break;
            case ForbiddenAreasType.DesignerForbiddenArea:
                DesignerForbiddenAreasList.Add(aiShape);
                break;
            case ForbiddenAreasType.ForbiddenBoundaries:
                ForbiddenBoundariesList.Add(aiShape);
                break;
            case ForbiddenAreasType.ExtraLink:
                ExtraLinkCostsList.Add(aiShape);
                break;
            default:
                throw new GameException($"Invalid forbiddenAreaType {forbiddenArea}");
        }

    }
}
