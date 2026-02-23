using System.Xml.Serialization;

namespace CgfConverter.Models.Materials;

[XmlRoot(ElementName = "Material")]
public class Material
{
    public Color? DiffuseValue;
    public Color? SpecularValue;
    public Color? EmissiveValue;
    public float? OpacityValue;
    public MaterialFlags MaterialFlags;

    [XmlIgnore]
    internal string? SourceFileName { get; set; }

    [XmlAttribute(AttributeName = "Name")]
    public string? Name { get; set; } = string.Empty;

    [XmlAttribute(AttributeName = "MtlFlags")]
    public string? MtlFlags {
        get => MaterialFlags == 0 ? null : ((int)MaterialFlags).ToString();
        set => MaterialFlags = value is null ? 0 : (MaterialFlags)int.Parse(value);
    }

    [XmlAttribute(AttributeName = "Shader")]
    public string? Shader { get; set; }

    [XmlAttribute(AttributeName = "GenMask")]
    public string? GenMask { get; set; }

    [XmlAttribute(AttributeName = "StringGenMask")]
    public string? StringGenMask { get; set; }

    [XmlAttribute(AttributeName = "SurfaceType")]
    public string? SurfaceType { get; set; }

    [XmlAttribute(AttributeName = "MatTemplate")]
    public string? MatTemplate { get; set; }

    [XmlAttribute(AttributeName = "Diffuse")]
    public string? Diffuse {
        get { return Color.Serialize(DiffuseValue); }
        set { DiffuseValue = Color.Deserialize(value); }
    }

    [XmlAttribute(AttributeName = "Specular")]
    public string? Specular {
        get { return Color.Serialize(SpecularValue); }
        set { SpecularValue = Color.Deserialize(value); }
    }

    [XmlAttribute(AttributeName = "Emissive")]
    public string? Emissive {
        get { return Color.Serialize(EmissiveValue); ; }
        set { EmissiveValue = Color.Deserialize(value); }
    }

    [XmlAttribute(AttributeName = "Shininess")]
    public double Shininess { get; set; }

    [XmlAttribute(AttributeName = "Opacity")]
    public string? Opacity {
        get { return OpacityValue.ToString(); }
        set { OpacityValue = float.Parse(value ?? "1"); }
    }

    [XmlAttribute(AttributeName = "Glossiness")]
    public double Glossiness { get; set; }

    [XmlAttribute(AttributeName = "GlowAmount")]
    public double GlowAmount { get; set; }

    [XmlAttribute(AttributeName = "AlphaTest")]
    public double AlphaTest { get; set; }

    [XmlArray(ElementName = "SubMaterials")]
    [XmlArrayItem(ElementName = "Material")]
    public Material[]? SubMaterials { get; set; }

    [XmlElement(ElementName = "PublicParams")]
    public PublicParams? PublicParams { get; set; }

    // TODO: TimeOfDay Support

    [XmlArray(ElementName = "Textures")]
    [XmlArrayItem(ElementName = "Texture")]
    public Texture[]? Textures { get; set; }

    public override string ToString() => $"Name: {Name}, Shader: {Shader}, Submaterials: {SubMaterials?.Length ?? 0}";
}
