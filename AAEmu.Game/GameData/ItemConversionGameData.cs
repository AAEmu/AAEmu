using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class ItemConversionGameData : Singleton<ItemConversionGameData>, IGameDataLoader
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        private List<ItemConversionReagent> _reagents;
        private List<ItemConversionProduct> _products;
        private List<int> _conversions;
        private Dictionary<uint, int> _conversionSets;

        public ItemConversionReagent GetReagentForItem(byte grade, ItemImplEnum implId, uint itemId, int level)
        {
            // figure out if there is a conversion for this specific item id first
            foreach (var reagent in _reagents)
            {
                if (itemId == reagent.InputItemId)
                {
                    if (grade >= reagent.MinItemGrade && grade <= reagent.MaxItemGrade)
                    {
                        return reagent;
                    }
                }
            }
            foreach (var reagent in _reagents)
            {
                if (implId == reagent.ImplId && grade >= reagent.MinItemGrade && grade <= reagent.MinItemGrade
                        && level >= reagent.MinLevel && level <= reagent.MaxLevel)
                {
                    return reagent;
                }
            }
            return null;
        }

        public ItemConversionProduct GetProductFromReagent(ItemConversionReagent convReagent)
        {
            foreach (var product in _products)
            {
                if (product.ConversionId == convReagent.ConversionId)
                {
                    return product;
                }
            }
            return null;
        }

        public bool IsValidConversionSet(int opcode, ItemConversionReagent reagent)
        {
            return _conversions.Contains(opcode) && reagent.ConversionId == opcode;
        }

        public void Load(SqliteConnection connection)
        {
            _reagents = new List<ItemConversionReagent>();
            _products = new List<ItemConversionProduct>();
            _conversions = new List<int>();
            _conversionSets = new Dictionary<uint, int>();

            // reagents
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_conv_reagent_filters";
                command.Prepare();

                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        ItemConversionReagent data = new ItemConversionReagent()
                        {
                            ConversionId = reader.GetUInt32("item_conv_rpack_id"),
                            ImplId = (ItemImplEnum) reader.GetInt32("item_impl_id"),
                            MinLevel = reader.GetInt32("min_level"),
                            MaxLevel = reader.GetInt32("max_level"),
                            MinItemGrade = reader.GetByte("item_grade_id"),
                            MaxItemGrade = reader.GetByte("max_item_grade_id")
                        };
                        _reagents.Add(data);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_conv_reagents";
                command.Prepare();

                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        ItemConversionReagent data = new ItemConversionReagent()
                        {
                            ConversionId = reader.GetUInt32("item_conv_rpack_id"),
                            InputItemId = reader.GetUInt32("item_id"),
                            MinItemGrade = reader.GetByte("grade_id"),
                            MaxItemGrade = reader.GetByte("max_grade_id")
                        };
                        _reagents.Add(data);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_conv_sets";
                command.Prepare();

                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        _conversions.Add(reader.GetInt32("id"));
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_convs";
                command.Prepare();
                
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        _conversions.Add(reader.GetInt32("item_conv_set_id", -1));
                    }
                }
            }

            // products
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT base.id, base.item_conv_ppack_id, A.chance_rate, base.item_id, base.weight, base.min, base.max FROM item_conv_products AS base LEFT JOIN item_conv_ppacks AS A ON base.item_conv_ppack_id=A.id";
                command.Prepare();

                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        ItemConversionProduct data = new ItemConversionProduct()
                        {
                            ConversionId = reader.GetUInt32("item_conv_ppack_id"),
                            ChanceRate = reader.GetInt32("chance_rate", 0),
                            OuputItemId = reader.GetUInt32("item_id"),
                            Weight = reader.GetInt32("weight"),
                            MinOutput = reader.GetInt32("min"),
                            MaxOutput = reader.GetInt32("max")
                        };
                        _products.Add(data);
                    }
                }
            }
        }

        public void PostLoad()
        {
            foreach (var set in _conversionSets)
            {
                foreach (var reagent in _reagents)
                {
                    if (reagent.ConversionId == set.Key)
                        reagent.ConversionSet = set.Value;
                }
            }
            _conversionSets.Clear();
            _conversionSets = null;
        }
    }
}