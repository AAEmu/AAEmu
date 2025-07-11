using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Entities;

internal class LinkDescriptor
{
    public long SourceNode { get; set; }
    public long TargetNode { get; set; }
    public Vector3 EdgeCenter { get; set; } = Vector3.Zero;
    public double MaxPassRadius { get; set; }
    public double Exposure { get; set; }
    public double Length { get; set; }
    public double MaxWaterDepth { get; set; }
    public double MinWaterDepth { get; set; }
    public byte StartIndex { get; set; }
    public byte EndIndex { get; set; }
    public bool IsPureTriangularLink { get; set; }
    public bool SimplePassabilityCheck { get; set; }

    public bool Equals(LinkDescriptor other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return SourceNode == other.SourceNode &&
               TargetNode == other.TargetNode &&
               MaxPassRadius.Equals(other.MaxPassRadius) &&
               Exposure.Equals(other.Exposure) &&
               Length.Equals(other.Length) &&
               MaxWaterDepth.Equals(other.MaxWaterDepth) &&
               MinWaterDepth.Equals(other.MinWaterDepth) &&
               StartIndex == other.StartIndex &&
               EndIndex == other.EndIndex &&
               IsPureTriangularLink == other.IsPureTriangularLink &&
               SimplePassabilityCheck == other.SimplePassabilityCheck &&
               Vector3.Equals(EdgeCenter, other.EdgeCenter);
    }
}
