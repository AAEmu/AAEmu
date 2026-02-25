// Quick script to query costume items from compact.sqlite3
// Run with: dotnet run --project AAEmu.Game -- or as standalone

using System;
using System.IO;

// We'll use a simple approach - read the sqlite file via the game's own code
// But since we can't easily run that, let's create a minimal console app

Console.WriteLine("Use this SQL in any SQLite viewer on compact.sqlite3:");
Console.WriteLine();
Console.WriteLine("SELECT ia.item_id, ia.slot_type_id FROM item_armors ia WHERE ia.slot_type_id = 31 LIMIT 50;");
Console.WriteLine();
Console.WriteLine("Or for items table with category 10 (Costume):");
Console.WriteLine("SELECT id, category_id, level FROM items WHERE category_id = 10 LIMIT 50;");
