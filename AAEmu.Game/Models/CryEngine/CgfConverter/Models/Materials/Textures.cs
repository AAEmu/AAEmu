using System.Collections.Generic;
using System.Xml.Serialization;

namespace CgfConverter.Models.Materials;

[XmlRoot(ElementName = "Textures")]
public record Textures
{
    [XmlElement(ElementName = "Texture")]
    public readonly List<Texture> Texture = new();
}
