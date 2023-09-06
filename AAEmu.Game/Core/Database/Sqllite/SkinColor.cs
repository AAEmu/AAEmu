using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkinColor
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public long? BrightSkinColorR { get; set; }

    public long? BrightSkinColorG { get; set; }

    public long? BrightSkinColorB { get; set; }

    public string Comment { get; set; }

    public string CustomPostfix { get; set; }

    public long? MiddleSkinColorR { get; set; }

    public long? MiddleSkinColorG { get; set; }

    public long? MiddleSkinColorB { get; set; }

    public byte[] NpcOnly { get; set; }

    public long? DiffuseColorR { get; set; }

    public long? DiffuseColorG { get; set; }

    public long? DiffuseColorB { get; set; }

    public long? SpecularColorR { get; set; }

    public long? SpecularColorG { get; set; }

    public long? SpecularColorB { get; set; }

    public long? Glossness { get; set; }

    public double? SpecularLevel { get; set; }
}
