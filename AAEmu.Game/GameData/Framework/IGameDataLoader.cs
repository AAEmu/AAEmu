﻿using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData.Framework;

public interface IGameDataLoader
{
    //static T Instance { get; set; }
    void Load(SqliteConnection connection, SqliteConnection connection2);
    void PostLoad();
}
