using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Friendslist
/// </summary>
public partial class Friend
{
    public int Id { get; set; }

    public int FriendId { get; set; }

    public int Owner { get; set; }
}
