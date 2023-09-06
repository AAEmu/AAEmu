using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Table containing SQL update script information
/// </summary>
public partial class Update
{
    public string ScriptName { get; set; }

    public sbyte Installed { get; set; }

    public DateTime InstallDate { get; set; }

    public string LastError { get; set; }
}
