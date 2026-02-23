namespace CgfConverter.CryXmlB;

public sealed record CryXmlReference
{
    public int NameOffset { get; init; }
    public int ValueOffset { get; init; }
}
