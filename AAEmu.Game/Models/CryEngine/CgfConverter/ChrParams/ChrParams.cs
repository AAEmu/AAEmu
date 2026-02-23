using System.Xml.Serialization;

namespace CgfConverter.ChrParams;

[XmlRoot(ElementName = "Params")]
public class ChrParams
{
    [XmlIgnore]
    internal string? SourceFileName { get; set; }

    [XmlArray(ElementName = "AnimationList")]
    [XmlArrayItem(ElementName = "Animation")]
    public Animation[]? Animations { get; set; }
}