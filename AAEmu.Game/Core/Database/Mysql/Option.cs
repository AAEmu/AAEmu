using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Settings that the client stores on the server
/// </summary>
public partial class Option
{
    public string Key { get; set; }

    public string Value { get; set; }

    public uint Owner { get; set; }
}
