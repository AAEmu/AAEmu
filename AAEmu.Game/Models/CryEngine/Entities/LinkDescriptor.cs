using System.Numerics;
using AAEmu.Game.Models.CryEngine.Readers;

namespace AAEmu.Game.Models.CryEngine.Entities;

public class LinkDescriptor(NetMissionReader baiReader)
{
    public NetMissionReader BaiReader { get; } = baiReader;
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

    // Helper references
    public NodeDescriptor SourceNodeDescriptor { get; set; }
    public NodeDescriptor TargetNodeDescriptor { get; set; }

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
#if NET10_0_OR_GREATER
              Vector3.EqualsAll(EdgeCenter, other.EdgeCenter);
#else
              Vector3.Equals(EdgeCenter, other.EdgeCenter);
#endif
    }
}
