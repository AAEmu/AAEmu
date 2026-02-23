using System.Xml.Serialization;

namespace CgfConverter.ChrParams;

[XmlRoot(ElementName = "Animation")]
public class Animation
{
    [XmlAttribute(AttributeName = "name")]
    public string? Name { get; set; }
    
    [XmlAttribute(AttributeName = "path")]
    public string? Path { get; set; }
}