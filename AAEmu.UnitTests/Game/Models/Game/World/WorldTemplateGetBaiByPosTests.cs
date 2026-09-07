using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldTemplateGetBaiByPosTests
{
    private const uint ZoneWest = 100u;
    private const uint ZoneEast = 200u;

    [Test]
    public async Task GetBaiByPos_WhenTwoZoneBaiLoaded_ReturnsBaiMatchingRegionZoneKey()
    {
        var template = CreateTwoZoneWorldTemplate();

        var westPos = new Vector3(32f, 32f, 100f);
        var eastPos = new Vector3(96f, 32f, 100f);

        var westBai = template.GetBaiByPos(westPos);
        var eastBai = template.GetBaiByPos(eastPos);

        await Assert.That(westBai).IsSameReferenceAs(template.ZoneBaiLoader[ZoneWest]);
        await Assert.That(eastBai).IsSameReferenceAs(template.ZoneBaiLoader[ZoneEast]);
        await Assert.That(westBai).IsNotSameReferenceAs(eastBai);
    }

    [Test]
    public async Task GetBaiByPos_WhenSameRegion_ReturnsSameZoneBai()
    {
        var template = CreateTwoZoneWorldTemplate();
        var baiA = template.GetBaiByPos(new Vector3(10f, 10f, 50f));
        var baiB = template.GetBaiByPos(new Vector3(50f, 50f, 50f));

        await Assert.That(baiA).IsSameReferenceAs(baiB);
        await Assert.That(baiA).IsSameReferenceAs(template.ZoneBaiLoader[ZoneWest]);
    }

    [Test]
    public async Task GetBaiByPos_WhenRegionZoneKeyMissingAndNoNavNodes_ReturnsNull()
    {
        var template = CreateTwoZoneWorldTemplate();
        template.ZoneKeyByRegions[0, 0] = 999u;

        var bai = template.GetBaiByPos(new Vector3(8f, 8f, 10f));

        await Assert.That(bai).IsNull();
    }

    [Test]
    public async Task GetBaiByPos_WhenOutOfBoundsAndNoNavNodes_ReturnsNull()
    {
        var template = CreateTwoZoneWorldTemplate();

        var bai = template.GetBaiByPos(new Vector3(-100f, -100f, 0f));

        await Assert.That(bai).IsNull();
    }

    private static WorldTemplate CreateTwoZoneWorldTemplate()
    {
        var sectorCount = 2 * WorldManager.SECTORS_PER_CELL;
        var template = new WorldTemplate
        {
            Name = "test_zone_world",
            CellX = 2,
            CellY = 2,
            ZoneKeyByRegions = new uint[sectorCount, sectorCount]
        };

        template.ZoneKeyByRegions[0, 0] = ZoneWest;
        template.ZoneKeyByRegions[1, 0] = ZoneEast;

        template.ZoneBaiLoader.Add(ZoneWest, new BaseBaiLoader(template));
        template.ZoneBaiLoader.Add(ZoneEast, new BaseBaiLoader(template));

        return template;
    }
}
