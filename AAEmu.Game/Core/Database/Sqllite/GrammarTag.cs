using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GrammarTag
{
    public long? Id { get; set; }

    public long? Idx { get; set; }

    public long? Tagid { get; set; }

    public string GrammartagG { get; set; }

    public string GrammartagA { get; set; }

    public string GrammartagI { get; set; }

    public string GrammartagPl { get; set; }

    public string GrammartagPlg { get; set; }

    public string GrammartagPld { get; set; }

    public string GrammartagPla { get; set; }
}
