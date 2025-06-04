using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.XML;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class TransferGameData : Singleton<TransferGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    
    /// <summary>
    /// List of TransferTemplates by TemplateId
    /// </summary>
    private Dictionary<uint, TransferTemplate> _templates = [];

    /// <summary>
    /// Lists of roads by world template (worldTemplateId, roads)
    /// </summary>
    private Dictionary<byte, Dictionary<uint, List<TransferRoads>>> _transferRoads = [];

    public void Load(SqliteConnection connection)
    {
        _templates = [];

        #region SQLite
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM transfers";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TransferTemplate();

                    template.Id = reader.GetUInt32("id"); // OwnerId
                    template.Name = LocalizationManager.Instance.Get("transfers", "comment", reader.GetUInt32("id"),
                        reader.GetString("comment"));
                    template.ModelId = reader.GetUInt32("model_id");
                    template.WaitTime = reader.GetFloat("wait_time");
                    template.Cyclic = reader.GetBoolean("cyclic", true);
                    template.PathSmoothing = reader.GetFloat("path_smoothing");

                    _templates.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM transfer_bindings";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TransferBindings
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        AttachPointId = (AttachPointKind)reader.GetInt16("attach_point_id"),
                        TransferId = reader.GetUInt32("transfer_id")
                    };
                    if (_templates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.TransferBindings.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM transfer_binding_doodads";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TransferBindingDoodads
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id"),
                        DoodadId = reader.GetUInt32("doodad_id"),
                    };
                    if (_templates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.TransferBindingDoodads.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM transfer_paths";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TransferPaths
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        PathName = reader.GetString("path_name"),
                        WaitTimeStart = reader.GetDouble("wait_time_start"),
                        WaitTimeEnd = reader.GetDouble("wait_time_end")
                    };
                    if (_templates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.TransferAllPaths.Add(template);
                    }
                }
            }
        }
        #endregion SQLite

        #region TransferPathXml
        Logger.Info("Loading transfer_path...");
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        //                              worldId           key  transfer_path
        _transferRoads = [];
        foreach (var (worldName, worldTemplate) in WorldManager.Instance.WorldTemplates)
        {
            var transferPaths = new Dictionary<uint, List<TransferRoads>>();

            var worldLevelDesignDir = Path.Combine("game", "worlds", worldTemplate.Name, "level_design", "zone");
            var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "transfer_path.xml", true);

            foreach (var pathFileName in pathFiles)
            {
                if (!uint.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(pathFileName))),
                        out var zoneId))
                {
                    Logger.Warn($"Unable to parse zoneId from {pathFileName}");
                    continue;
                }

                var contents = ClientFileManager.GetFileAsString(pathFileName);

                if (string.IsNullOrWhiteSpace(contents))
                {
                    Logger.Warn($"{pathFileName} doesn't exists or is empty.");
                    continue;
                }

                Logger.Debug($"Loading {pathFileName}");

                var transferPath = new List<TransferRoads>();
                var xDoc = new XmlDocument();
                xDoc.LoadXml(contents);
                var xRoot = xDoc.DocumentElement;
                if (xRoot != null)
                {
                    foreach (XmlElement xNode in xRoot)
                    {
                        var transferRoad = new TransferRoads();
                        var transferAttribs = XmlHelper.ReadNodeAttributes(xNode);

                        transferRoad.ZoneId = zoneId;
                        transferRoad.Name = XmlHelper.ReadAttribute(transferAttribs, "Name", "");
                        transferRoad.Type = XmlHelper.ReadAttribute(transferAttribs, "Type", 0);
                        transferRoad.CellX = XmlHelper.ReadAttribute(transferAttribs, "cellX", 0);
                        transferRoad.CellY = XmlHelper.ReadAttribute(transferAttribs, "cellY", 0);

                        foreach (XmlNode childNode in xNode.ChildNodes)
                        {
                            foreach (XmlNode node in childNode.ChildNodes)
                            {
                                var posNodeAttribs = XmlHelper.ReadNodeAttributes(node);
                                if (posNodeAttribs.TryGetValue("Pos", out var attributeValue))
                                {
                                    var xyz = XmlHelper.StringToVector3(attributeValue);

                                    // конвертируем координаты из локальных в мировые, сразу при считывании из файла пути
                                    // convert coordinates from local to world, immediately when reading the path from the file
                                    var vec = ZoneManager.ConvertToWorldCoordinates(zoneId, xyz);
                                    var pos = new WorldSpawnPosition()
                                    {
                                        X = vec.X,
                                        Y = vec.Y,
                                        Z = vec.Z,
                                        WorldId = worldTemplate.Id,
                                        ZoneId = zoneId
                                    };
                                    transferRoad.Pos.Add(pos);
                                }
                            }
                        }

                        transferPath.Add(transferRoad);
                    }
                }

                transferPaths.Add(zoneId, transferPath);
            }

            _transferRoads.Add((byte)worldTemplate.Id, transferPaths);
            GetOwnerPaths(worldTemplate.Id);
        }

        #endregion TransferPathXml
    }

    public void PostLoad()
    {
        //
    }
    
    /// <summary>
    /// Get a list of all parts of your path for transportation to populate the Template TransferRoads
    /// </summary>
    /// <param name="worldTemplateId"></param>
    /// <returns></returns>
    private void GetOwnerPaths(uint worldTemplateId = 0)
    {
        foreach (var (id, transferTemplate) in _templates)
        {
            foreach (var transferPaths in transferTemplate.TransferAllPaths)
            {
                foreach (var (wid, transfers) in _transferRoads)
                {
                    if (wid != worldTemplateId) { continue; }
                    foreach (var (zid, transfer) in transfers)
                    {
                        foreach (var path in transfer.Where(path => path.Name == transferPaths.PathName))
                        {
                            if (transferTemplate.TransferRoads.Any(tr => tr.Name == transferPaths.PathName))
                                continue;

                            var tmp = new TransferRoads
                            {
                                Name = path.Name,
                                Type = path.Type,
                                CellX = path.CellX,
                                CellY = path.CellY,
                                Pos = path.Pos
                            };
                            transferTemplate.TransferRoads.Add(tmp);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a given Transfer Template exists
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public bool Exist(uint templateId)
    {
        return _templates.ContainsKey(templateId);
    }

    /// <summary>
    /// Gets Transfer Template by Id
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public TransferTemplate GetTransferTemplate(uint templateId)
    {
        return _templates.GetValueOrDefault(templateId);
    }
}
