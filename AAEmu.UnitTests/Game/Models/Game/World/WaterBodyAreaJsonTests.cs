using AAEmu.Game.Models.Game.World;
using Jitter2.LinearMath;
using Newtonsoft.Json;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Pins the JSON shape of <see cref="WaterBodyArea.Points"/> so changing the point type cannot change saved data.
/// </summary>
public class WaterBodyAreaJsonTests
{
    [Test]
    public async Task SerializeObject_WithPoints_WritesXYZObjects()
    {
        // Arrange
        var area = new WaterBodyArea("lake", WaterBodyAreaType.Polygon);
        area.Points.Add(new JVector(1.5f, -2f, 100f));

        // Act
        var json = JsonConvert.SerializeObject(area);

        // Assert
        await Assert.That(json).Contains("\"Points\":[{\"X\":1.5,\"Y\":-2.0,\"Z\":100.0}]");
    }

    [Test]
    public async Task DeserializeObject_WithPoints_ReadsXYZObjects()
    {
        // Arrange
        const string json = "{\"Name\":\"lake\",\"Points\":[{\"X\":1.5,\"Y\":-2.0,\"Z\":100.0},{\"X\":3,\"Y\":4,\"Z\":5}]}";

        // Act
        var area = JsonConvert.DeserializeObject<WaterBodyArea>(json);

        // Assert
        await Assert.That(area).IsNotNull();
        await Assert.That(area!.Points.Count).IsEqualTo(2);
        await Assert.That(area.Points[0].X).IsEqualTo(1.5f);
        await Assert.That(area.Points[0].Y).IsEqualTo(-2f);
        await Assert.That(area.Points[0].Z).IsEqualTo(100f);
        await Assert.That(area.Points[1].Z).IsEqualTo(5f);
    }
}
