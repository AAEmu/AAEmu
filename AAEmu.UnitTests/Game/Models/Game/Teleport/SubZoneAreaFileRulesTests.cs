using System.Numerics;

using AAEmu.Game.Models.Game.Teleport;

namespace AAEmu.UnitTests.Game.Models.Game.Teleport;

public class SubZoneAreaFileRulesTests
{
    [Test]
    public async Task Parse_ReadsAreaIdAndLocalPolygon()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Objects>
              <Entity Pos="100,200,0" cellX="0" cellY="0">
                <Area Id="42">
                  <Points>
                    <Point Pos="0,0,0" />
                    <Point Pos="10,0,0" />
                    <Point Pos="10,10,0" />
                    <Point Pos="0,10,0" />
                  </Points>
                </Area>
              </Entity>
            </Objects>
            """;

        var areas = SubZoneAreaFileRules.Parse(xml, 183);
        await Assert.That(areas.Count).IsEqualTo(1);
        await Assert.That(areas[0].ZoneKey).IsEqualTo(183u);
        await Assert.That(areas[0].AreaId).IsEqualTo(42u);
        await Assert.That(areas[0].Points.Count).IsEqualTo(4);
        await Assert.That(areas[0].Points[0].X).IsEqualTo(100f);
        await Assert.That(areas[0].Points[0].Y).IsEqualTo(200f);
        await Assert.That(areas[0].Points[2].X).IsEqualTo(110f);
        await Assert.That(areas[0].Points[2].Y).IsEqualTo(210f);
    }

    [Test]
    public async Task Parse_AppliesCellOffset()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Objects>
              <Entity Pos="0,0,0" cellX="1" cellY="2">
                <Area Id="7">
                  <Points>
                    <Point Pos="1,1,0" />
                    <Point Pos="2,1,0" />
                    <Point Pos="2,2,0" />
                  </Points>
                </Area>
              </Entity>
            </Objects>
            """;

        var areas = SubZoneAreaFileRules.Parse(xml, 10);
        await Assert.That(areas.Count).IsEqualTo(1);
        // cellX*1024 + 1, cellY*1024 + 1
        await Assert.That(areas[0].Points[0].X).IsEqualTo(1025f);
        await Assert.That(areas[0].Points[0].Y).IsEqualTo(2049f);
    }

    [Test]
    public async Task Parse_SkipsZeroZoneAndEmptyInput()
    {
        await Assert.That(SubZoneAreaFileRules.Parse("<Objects/>", 0).Count).IsEqualTo(0);
        await Assert.That(SubZoneAreaFileRules.Parse(null, 1).Count).IsEqualTo(0);
        await Assert.That(SubZoneAreaFileRules.Parse("  ", 1).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_MalformedXml_Throws()
    {
        await Assert.That(() => SubZoneAreaFileRules.Parse("not xml", 1))
            .Throws<System.Xml.XmlException>();
    }

    [Test]
    public async Task Catalog_MalformedXml_IsSkippedWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), "aaemu-sz-bad-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "worlds", "main_world", "level_design", "zone", "183", "client");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "subzone_area.xml"), "not xml");
        try
        {
            var areas = SubZoneAreaGCatalog.LoadFromRoots([root]);
            await Assert.That(areas.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Contains_EvenOddAndEdge()
    {
        var square = new List<Vector2>
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10)
        };
        await Assert.That(SubZoneAreaFileRules.Contains(square, 5, 5)).IsTrue();
        await Assert.That(SubZoneAreaFileRules.Contains(square, 15, 5)).IsFalse();
        await Assert.That(SubZoneAreaFileRules.Contains(square, 0, 5)).IsTrue(); // on edge
        await Assert.That(SubZoneAreaFileRules.Contains(null, 5, 5)).IsFalse();
        await Assert.That(SubZoneAreaFileRules.Contains([new(0, 0), new(1, 1)], 0.5f, 0.5f)).IsFalse();
    }

    [Test]
    public async Task Catalog_ReadsClientSubzoneFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "aaemu-sz-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "worlds", "main_world", "level_design", "zone", "183", "client");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "subzone_area.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Objects>
              <Entity Pos="0,0,0" cellX="0" cellY="0">
                <Area Id="55">
                  <Points>
                    <Point Pos="0,0,0" />
                    <Point Pos="1,0,0" />
                    <Point Pos="1,1,0" />
                  </Points>
                </Area>
              </Entity>
            </Objects>
            """);
        try
        {
            var areas = SubZoneAreaGCatalog.LoadFromRoots([root]);
            await Assert.That(areas.Count).IsEqualTo(1);
            await Assert.That(areas[0].ZoneKey).IsEqualTo(183u);
            await Assert.That(areas[0].AreaId).IsEqualTo(55u);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
