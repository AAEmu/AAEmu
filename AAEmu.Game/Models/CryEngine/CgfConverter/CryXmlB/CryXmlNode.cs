namespace CgfConverter.CryXmlB;

public sealed record CryXmlNode
{
    public int NodeID { get; init; }
    public int NodeNameOffset { get; init; }
    public int ItemType { get; init; }
    public short AttributeCount { get; init; }
    public short ChildCount { get; init; }
    public int ParentNodeID { get; init; }
    public int FirstAttributeIndex { get; init; }
    public int FirstChildIndex { get; init; }
    public int Reserved { get; init; }
}
