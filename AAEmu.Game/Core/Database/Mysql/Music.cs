using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// User Created Content (music)
/// </summary>
public partial class Music
{
    public int Id { get; set; }

    /// <summary>
    /// PlayerId
    /// </summary>
    public int Author { get; set; }

    public string Title { get; set; }

    /// <summary>
    /// Song MML
    /// </summary>
    public string Song { get; set; }
}
