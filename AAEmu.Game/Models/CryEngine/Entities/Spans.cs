namespace AAEmu.Game.Models.CryEngine.Entities;

// Actual name should be Span, but called it Spans to avoid syntax errors.
internal class Spans
{
    public double X { get; set; }
    public double Y { get; set; }
    public double MinZ { get; set; }
    public double MaxZ { get; set; }
    public double MaxRadius { get; set; }
    public int Classification { get; set; }
    public int ChildIdx { get; set; }
    public int NextIdx { get; set; }

    public bool Equals(Spans other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return (X.Equals(other.X) &&
                Y.Equals(other.Y) &&
                MinZ.Equals(other.MinZ) &&
                MaxZ.Equals(other.MaxZ) &&
                MaxRadius.Equals(other.MaxRadius) &&
                Classification.Equals(other.Classification) &&
                ChildIdx.Equals(other.ChildIdx) &&
                NextIdx.Equals(other.NextIdx));
    }
}
