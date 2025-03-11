using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionSold : PacketMarshaler
{
    public int Id { get; set; } // ключ
    public uint ItemId { get; set; } // Id
    public int Day { get; set; }
    public long MinCopper { get; set; }
    public long MaxCopper { get; set; }
    public long AvgCopper { get; set; }
    public int Volume { get; set; }
    public byte ItemGrade { get; set; }
    public long WeeklyAvgCopper { get; set; }
    public List<AuctionSold> Solds { get; set; }

    public AuctionSold()
    {
        Solds = new List<AuctionSold>();
    }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadUInt32();
        Day = stream.ReadInt32();
        MinCopper = stream.ReadInt64();
        MaxCopper = stream.ReadInt64();
        AvgCopper = stream.ReadInt64();
        Volume = stream.ReadInt32();
        ItemGrade = stream.ReadByte();
        WeeklyAvgCopper = stream.ReadInt64();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ItemId);
        stream.Write(Day);
        stream.Write(MinCopper);
        stream.Write(MaxCopper);
        stream.Write(AvgCopper);
        stream.Write(Volume);
        stream.Write(ItemGrade);
        stream.Write(WeeklyAvgCopper);
        return stream;
    }

    public void SaveAuctionSold(MySqlConnection connection, AuctionSold auctionSold)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO auction_sold (item_id, day, min_copper, max_copper, avg_copper, volume, item_grade, weekly_avg_copper)" +
                              " VALUES (@item_id, @day, @min_copper, @max_copper, @avg_copper, @volume, @item_grade, @weekly_avg_copper);";
        command.Prepare();
        command.Parameters.AddWithValue("@item_id", auctionSold.ItemId);
        command.Parameters.AddWithValue("@day", auctionSold.Day);
        command.Parameters.AddWithValue("@min_copper", auctionSold.MinCopper);
        command.Parameters.AddWithValue("@max_copper", auctionSold.MaxCopper);
        command.Parameters.AddWithValue("@avg_copper", auctionSold.AvgCopper);
        command.Parameters.AddWithValue("@volume", auctionSold.Volume);
        command.Parameters.AddWithValue("@item_grade", auctionSold.ItemGrade);
        command.Parameters.AddWithValue("@weekly_avg_copper", auctionSold.WeeklyAvgCopper);

        command.ExecuteNonQuery();
    }

    public AuctionSold GetAuctionSoldByItemId(MySqlConnection connection, uint itemId)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
                SELECT id, item_id, day, min_copper, max_copper, avg_copper, volume, item_grade, weekly_avg_copper
                FROM auction_sold
                WHERE item_id = @item_id;
            ";
        command.Parameters.AddWithValue("@item_id", itemId);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new AuctionSold
            {
                Id = reader.GetInt32("id"),
                ItemId = reader.GetUInt32("item_id"),
                Day = reader.GetInt32("day"),
                MinCopper = reader.GetInt64("min_copper"),
                MaxCopper = reader.GetInt64("max_copper"),
                AvgCopper = reader.GetInt64("avg_copper"),
                Volume = reader.GetInt32("volume"),
                ItemGrade = reader.GetByte("item_grade"),
                WeeklyAvgCopper = reader.GetInt64("weekly_avg_copper")
            };
        }

        return null;
    }

    public List<AuctionSold> GetAllAuctionSold(MySqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var auctionSolds = new List<AuctionSold>();

        using var command = connection.CreateCommand();
        command.CommandText = @"
                SELECT id, item_id, day, min_copper, max_copper, avg_copper, volume, item_grade, weekly_avg_copper
                FROM auction_sold;
            ";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            auctionSolds.Add(new AuctionSold
            {
                Id = reader.GetInt32("id"),
                ItemId = reader.GetUInt32("item_id"),
                Day = reader.GetInt32("day"),
                MinCopper = reader.GetInt64("min_copper"),
                MaxCopper = reader.GetInt64("max_copper"),
                AvgCopper = reader.GetInt64("avg_copper"),
                Volume = reader.GetInt32("volume"),
                ItemGrade = reader.GetByte("item_grade"),
                WeeklyAvgCopper = reader.GetInt64("weekly_avg_copper")
            });
        }

        return auctionSolds;
    }

    public void DeleteAuctionSoldByItemId(MySqlConnection connection, uint itemId)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
                DELETE FROM auction_sold
                WHERE ItemId = @ItemId;
            ";
        command.Parameters.AddWithValue("@item_id", itemId);

        command.ExecuteNonQuery();
    }

    public void DeleteExcessAuctionSoldByItemId(MySqlConnection connection, uint itemId)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = @"
                SELECT id
                FROM auction_sold
                WHERE item_id = @item_id
                ORDER BY id DESC;
            ";
        selectCommand.Parameters.AddWithValue("@item_id", itemId);

        var idsToDelete = new List<int>();

        using (var reader = selectCommand.ExecuteReader())
        {
            var count = 0;
            while (reader.Read())
            {
                if (count >= 14)
                {
                    idsToDelete.Add(reader.GetInt32("id"));
                }
                count++;
            }
        }

        if (idsToDelete.Count > 0)
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = "DELETE FROM auction_sold WHERE id IN (" + string.Join(",", idsToDelete) + ");";
            deleteCommand.ExecuteNonQuery();
        }
    }

    public AuctionSold GetLast14AuctionSoldByItemId(MySqlConnection connection, uint itemId)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = @"
                SELECT id, item_id, day, min_copper, max_copper, avg_copper, volume, item_grade, weekly_avg_copper
                FROM auction_sold
                WHERE item_id = @ItemId
                ORDER BY id DESC
                LIMIT 14;
            ";
        selectCommand.Parameters.AddWithValue("@item_id", itemId);

        var soldItems = new List<AuctionSold>();

        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                soldItems.Add(new AuctionSold
                {
                    Id = reader.GetInt32("id"),
                    ItemId = reader.GetUInt32("item_id"),
                    Day = reader.GetInt32("day"),
                    MinCopper = reader.GetInt64("min_copper"),
                    MaxCopper = reader.GetInt64("max_copper"),
                    AvgCopper = reader.GetInt64("avg_copper"),
                    Volume = reader.GetInt32("volume"),
                    ItemGrade = reader.GetByte("item_grade"),
                    WeeklyAvgCopper = reader.GetInt64("weekly_avg_copper")
                });
            }
        }

        if (soldItems.Count > 0)
        {
            return new AuctionSold
            {
                ItemId = itemId,
                Solds = soldItems.ToList()
            };
        }

        return null;
    }

    /*
       public static void Main()
       {
           List<int> previousWeekData = new List<int> { 5, 7, 9, 11, 13, 15, 17 };
           List<int> currentWeekData = new List<int> { 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36 };

           List<int> combinedData = new List<int>(previousWeekData);
           combinedData.AddRange(currentWeekData);

           List<double> sevenDayAverages = CalculateSevenDayAverages(combinedData);

           for (int i = 0; i < sevenDayAverages.Count; i++)
           {
               Console.WriteLine($"Day {i + 1}: {sevenDayAverages[i]:F2}");
           }
       }     
    */
    public static List<double> CalculateSevenDayAverages(List<int> data)
    {
        var averages = new List<double>();

        for (var i = 6; i < data.Count; i++)
        {
            double sum = 0;
            for (var j = i - 6; j <= i; j++)
            {
                sum += data[j];
            }
            averages.Add(sum / 7);
        }

        return averages;
    }
}
