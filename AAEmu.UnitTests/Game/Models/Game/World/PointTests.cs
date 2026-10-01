using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// <see cref="Point.IsInside"/> judges house spots and subzones. Positions sit on a grid, so the row a
/// point is tested along regularly runs exactly through a polygon corner or along an edge.
/// </summary>
public class PointTests
{
    // A diamond: the row y = 10 runs through its left and right corners.
    private static readonly Point[] Diamond =
    [
        new(0f, 10f, 0f),
        new(10f, 0f, 0f),
        new(20f, 10f, 0f),
        new(10f, 20f, 0f),
    ];

    // A square with a notch cut into the top, down to a corner at (10, 10).
    private static readonly Point[] Notched =
    [
        new(0f, 0f, 0f),
        new(20f, 0f, 0f),
        new(20f, 20f, 0f),
        new(10f, 10f, 0f),
        new(0f, 20f, 0f),
    ];

    private static readonly Point[] Square =
    [
        new(0f, 0f, 0f),
        new(10f, 0f, 0f),
        new(10f, 10f, 0f),
        new(0f, 10f, 0f),
    ];

    [Test]
    [Arguments(5f, 10f, true)]
    [Arguments(15f, 10f, true)]
    [Arguments(-5f, 10f, false)]
    [Arguments(25f, 10f, false)]
    public async Task RowThroughTwoCorners_IsJudgedByWhereThePointIs(float x, float y, bool expected)
    {
        await Assert.That(Point.IsInside(Diamond, Diamond.Length, new Point(x, y, 0f))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(5f, 10f, true)]
    [Arguments(15f, 10f, true)]
    [Arguments(-5f, 10f, false)]
    [Arguments(10f, 15f, false)]
    public async Task RowThroughACornerThatOnlyTouchesIt_IsJudgedByWhereThePointIs(float x, float y, bool expected)
    {
        await Assert.That(Point.IsInside(Notched, Notched.Length, new Point(x, y, 0f))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(5f, 0f, true)]
    [Arguments(10f, 5f, true)]
    [Arguments(-5f, 0f, false)]
    [Arguments(15f, 0f, false)]
    [Arguments(-5f, 5f, false)]
    public async Task PointOnTheLineOfAnEdge_IsInsideOnlyOnTheEdgeItself(float x, float y, bool expected)
    {
        await Assert.That(Point.IsInside(Square, Square.Length, new Point(x, y, 0f))).IsEqualTo(expected);
    }

    [Test]
    public async Task PointsLeftOfTheOldFixedRayEnd_AreJudgedToo()
    {
        // The old test cast its ray only as far as x = 1000, so from x = 500 it never reached an edge of
        // a polygon that spans past 1000.
        Point[] wide =
        [
            new(0f, 2000f, 0f),
            new(2100f, 2000f, 0f),
            new(2100f, 2100f, 0f),
            new(0f, 2100f, 0f),
        ];

        await Assert.That(Point.IsInside(wide, wide.Length, new Point(500f, 2050f, 0f))).IsTrue();
        await Assert.That(Point.IsInside(wide, wide.Length, new Point(2200f, 2050f, 0f))).IsFalse();
    }
}
