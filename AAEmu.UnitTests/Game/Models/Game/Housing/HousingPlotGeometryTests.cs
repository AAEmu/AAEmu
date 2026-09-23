using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

/// <summary>
/// Rotated plot-bounds math. The corpus documents the house record's "placement corners" and the
/// RotateHouse packet pair but not the client's exact point-in-plot formula, so the geometry under
/// test is the standard yaw rotation described in <see cref="HousingPlotGeometry"/>'s evidence note:
/// world = position + R(yaw) * local, local = R(-yaw) * (world - position).
/// </summary>
public sealed class HousingPlotGeometryTests
{
    private const float Radius = 10f;
    private const float HouseX = 100f;
    private const float HouseY = 200f;

    private static bool InPlot(float yaw, float x, float y) =>
        HousingPlotGeometry.ContainsPoint(Radius, yaw, HouseX, HouseY, x, y);

    [Test]
    public async Task YawZero_AxisAlignedSquareStillAcceptsInteriorAndRejectsOutside()
    {
        await Assert.That(InPlot(0f, 108f, 203f)).IsTrue(); // local (8, 3)
        await Assert.That(InPlot(0f, 111f, 200f)).IsFalse(); // local (11, 0): past the radius
    }

    [Test]
    public async Task Yaw90Degrees_PlotTurnsQuarterTurn_AcceptsRotatedInteriorRejectsOutside()
    {
        const float yaw = MathF.PI / 2f;
        // local (8, 3) rotated into world space is (97, 208)
        await Assert.That(InPlot(yaw, 97f, 208f)).IsTrue();
        // local (0, 11) rotated into world space is (89, 200): outside
        await Assert.That(InPlot(yaw, 89f, 200f)).IsFalse();
        // an unrotated-square interior point that leaves the rotated plot: local (0, 11) -> (111, 200)
        // maps back to local (0, -11)
        await Assert.That(InPlot(yaw, 111f, 200f)).IsFalse();
    }

    [Test]
    public async Task Yaw180Degrees_AcceptsRotatedInteriorRejectsOutside()
    {
        const float yaw = MathF.PI;
        await Assert.That(InPlot(yaw, 92f, 197f)).IsTrue(); // local (8, 3) doubled through the origin
        await Assert.That(InPlot(yaw, 89f, 200f)).IsFalse(); // local (11, 0) rotated 180
    }

    [Test]
    public async Task Yaw270Degrees_AcceptsRotatedInteriorRejectsOutside()
    {
        const float yaw = 3f * MathF.PI / 2f;
        await Assert.That(InPlot(yaw, 103f, 192f)).IsTrue(); // local (8, 3) rotated 270
        await Assert.That(InPlot(yaw, 100f, 189f)).IsFalse(); // local (0, 11) rotated 270 -> (100, 189)
    }

    [Test]
    public async Task ArbitraryYaw_DiffersFromTheOldAxisAlignedCheckOnBothSides()
    {
        const float yaw = MathF.PI / 4f;

        // Corner region: inside the axis-aligned square (9 <= 10, 9 <= 10) but local (12.73, 0),
        // so outside the 45-degree rotated plot — the axis-aligned math wrongly accepted this.
        await Assert.That(InPlot(yaw, 109f, 209f)).IsFalse();

        // Diagonal reach: outside the axis-aligned square (13 > 10) but local (9.19, -9.19), inside
        // the rotated plot — the axis-aligned math wrongly rejected this.
        await Assert.That(InPlot(yaw, 113f, 200f)).IsTrue();
    }

    [Test]
    public async Task WorldPoint_MapsLocalCornersThroughTheHouseYaw()
    {
        var unrotated = HousingPlotGeometry.WorldPoint(0f, HouseX, HouseY, Radius, Radius);
        await Assert.That(MathF.Round(unrotated.X, 3)).IsEqualTo(110f);
        await Assert.That(MathF.Round(unrotated.Y, 3)).IsEqualTo(210f);

        var quarter = HousingPlotGeometry.WorldPoint(MathF.PI / 2f, HouseX, HouseY, Radius, Radius);
        await Assert.That(MathF.Round(quarter.X, 3)).IsEqualTo(90f);
        await Assert.That(MathF.Round(quarter.Y, 3)).IsEqualTo(210f);

        var diagonal = HousingPlotGeometry.WorldPoint(MathF.PI / 4f, HouseX, HouseY, Radius, 0f);
        await Assert.That(MathF.Round(diagonal.X, 3)).IsEqualTo(107.071f);
        await Assert.That(MathF.Round(diagonal.Y, 3)).IsEqualTo(207.071f);
    }

    [Test]
    public async Task ContainsPoint_BorderIsInclusive()
    {
        await Assert.That(InPlot(0f, 110f, 200f)).IsTrue();
        await Assert.That(InPlot(0f, 110.01f, 200f)).IsFalse();
    }
}
