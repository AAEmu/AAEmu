using System.Diagnostics;
using System.IO;
using System;
using CgfConverter.CryXmlB;
using CgfConverter.Models.Materials;

namespace CgfConverter.Utils;

public static class MaterialUtilities
{
    public static Material? FromFile(string path, string? materialName) =>
        FromStream(new FileStream(path, FileMode.Open, FileAccess.Read), materialName, true);

    public static Material? FromStream(Stream stream, string? materialName, bool closeAfter = false)
    {
        try
        {
            var material = CryXmlSerializer.Deserialize<Material>(stream);

            // For basic materials, add the material to submaterials so that all materials are consistent.
            if (material.SubMaterials is null)
            {
                material.Name = materialName;
                material.SubMaterials = new Material[1];
                material.SubMaterials[0] = material;
            }

            return material;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("{0} failed deserialize - {1}", materialName, ex.Message);
        }
        finally
        {
            if (closeAfter)
                stream.Close();
        }

        return null;
    }

    public static Material CreateDefaultMaterial(string materialName, string diffuse = "0.5,0.5,0.5") =>
        new()
        {
            Name = materialName,
            Diffuse = diffuse,
            Specular = "1.0,1.0,1.0",
            Shininess = 0.2,
            Opacity = "1.0",
            Textures = Array.Empty<Texture>()
        };
}
