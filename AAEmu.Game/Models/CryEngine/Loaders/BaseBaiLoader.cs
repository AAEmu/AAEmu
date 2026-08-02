using System.IO;
using System.Numerics;
using AAEmu.Commons.Exceptions;
using NLog;
using AAEmu.Game.IO;
using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Readers;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.CryEngine.Loaders;

public class BaseBaiLoader(WorldTemplate parentWorldTemplate)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private WorldTemplate ParentWorldTemplate { get; } = parentWorldTemplate;
    public List<AreasMissionReader> AreasMissionReaders { get; } = [];
    public List<NetMissionReader> NetMissionReaders { get; } = [];
    public List<VertexMissionReader> VertexMissionReaders { get; } = [];

    /// <summary>
    /// Loads .bai files data from a given zone or path folder
    /// </summary>
    /// <param name="zoneOrPathsFolder"></param>
    /// <param name="additiveLoad"></param>
    /// <exception cref="GameException"></exception>
    public void LoadBaiFilesFromFolder(string zoneOrPathsFolder, bool additiveLoad = false)
    {
        var worldFolder = Path.Combine("game", "worlds", ParentWorldTemplate.Name);

        if (!additiveLoad)
            ClearData();

        Logger.Debug($"LoadBaiFilesFromFolder {zoneOrPathsFolder}");
        try
        {
            // AreasMission*.bai
            var areaFiles = GetFiles("areasmission*.bai", zoneOrPathsFolder);
            foreach (var areaFile in areaFiles)
            {
                // Try to get zone key from folder name
                var areaFolderName = Path.GetFileName(Path.GetDirectoryName(areaFile)) ?? "";

                if (string.IsNullOrWhiteSpace(areaFolderName))
                    continue;

                // Skip file if it doesn't exist anymore for whatever reason
                if (!ClientFileManager.FileExists(areaFile))
                    continue;

                //LabelLoading.Text = $"Areas: {fileIndex}/{areaFiles.Length}";
                //LabelLoading.Refresh();

                var (zoneKey, pathBlockX, pathBlockY) = GetZoneAndOffsetsByName(areaFolderName);
                var targetOffset = GetTargetOffsetByZoneOrPath(zoneKey, pathBlockX, pathBlockY);

                // Logger.Debug($"Areas File: {areaFile}");

                // Load all .bai files for data
                var fileStream = ClientFileManager.GetFileStream(areaFile);
                // Ignore files that are too small or null streams
                if (fileStream == null || fileStream.Length <= 20)
                {
                    fileStream?.Dispose();
                    continue;
                }

                try
                {
                    var area = new AreasMissionReader(fileStream, zoneKey);
                    area.ReaderPointOffset = targetOffset;
                    area.ReadFile();
                    AreasMissionReaders.Add(area);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Areas File Exception: {ex}, in {areaFile}, area offset {targetOffset}, skipping the rest of this file");
                }
                finally
                {
                    fileStream.Dispose();
                }
            }

            // NetMission*.bai
            var netFiles = GetFiles("netmission*.bai", zoneOrPathsFolder);
            foreach (var netFile in netFiles)
            {
                // Try to get zone key from folder name
                var netFolderName = Path.GetFileName(Path.GetDirectoryName(netFile)) ?? "";

                if (string.IsNullOrWhiteSpace(netFolderName))
                    continue;

                //LabelLoading.Text = $"Net: {fileIndex}/{netFiles.Length}";
                //LabelLoading.Refresh();

                var (zoneKey, pathBlockX, pathBlockY) = GetZoneAndOffsetsByName(netFolderName);
                var targetOffset = GetTargetOffsetByZoneOrPath(zoneKey, pathBlockX, pathBlockY);

                // Logger.Debug($"Net File: {netFile}");

                using var fs = ClientFileManager.GetFileStream(netFile);
                var net = new NetMissionReader(fs, zoneKey);
                try
                {
                    net.ReaderPointOffset = targetOffset;
                    net.ReadFile();
                    NetMissionReaders.Add(net);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Net File Exception: {ex}, in {netFile}");
                    // continue;
                }
            }

            // VertexMission*.bai
            var vertexFiles = GetFiles("vertsmission*.bai", zoneOrPathsFolder);
            foreach (var vertexFile in vertexFiles)
            {
                // Try to get zone key from folder name
                var vertexFolderName = Path.GetFileName(Path.GetDirectoryName(vertexFile)) ?? "";

                if (string.IsNullOrWhiteSpace(vertexFolderName))
                    continue;

                //LabelLoading.Text = $"Vertex: {fileIndex}/{vertexFiles.Length}";
                //LabelLoading.Refresh();

                var (zoneKey, pathBlockX, pathBlockY) = GetZoneAndOffsetsByName(vertexFolderName);
                var targetOffset = GetTargetOffsetByZoneOrPath(zoneKey, pathBlockX, pathBlockY);

                // Logger.Debug($"Vertex File: {vertexFile}");

                var fileStream = ClientFileManager.GetFileStream(vertexFile);
                if (fileStream == null)
                    continue;

                try
                {
                    var vertex = new VertexMissionReader(fileStream, zoneKey);
                    vertex.ReaderPointOffset = targetOffset;
                    vertex.ReadFile();
                    VertexMissionReaders.Add(vertex);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Vertex File Exception: {ex}, in {vertexFile}");
                }
                finally
                {
                    fileStream.Dispose();
                }
            }

            // hidemission*.bai holds AI concealment volumes: the Zone evaluates those against its
            // own NPCs, so World does not read them.
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message);
            throw new GameException($"Exception loading files from {zoneOrPathsFolder}: {ex.Message}");
        }

        return;

        // ZoneKey,PathX, PathY 
        (uint, uint, uint) GetZoneAndOffsetsByName(string folderName)
        {
            var pathBlockX = 0u;
            var pathBlockY = 0u;
            if (folderName.Contains("_"))
            {
                // This is a path folder, not a zone folder
                var sectorSplit = folderName.Split("_");
                if (sectorSplit.Length == 2)
                {
                    if (!uint.TryParse(sectorSplit[0], out pathBlockX))
                        pathBlockX = 0u;
                    if (!uint.TryParse(sectorSplit[1], out pathBlockY))
                        pathBlockY = 0u;
                }
            }

            if (!uint.TryParse(folderName, out var zoneKey))
                zoneKey = 0u;
            return (zoneKey, pathBlockX, pathBlockY);
        }

        string[] GetFiles(string searchPattern, string forZones)
        {
            var rootFolder = worldFolder;

            if (!string.IsNullOrWhiteSpace(forZones))
            {
                rootFolder = Path.Combine(rootFolder, forZones.Contains('_') ? "paths" : "zone", forZones);
            }

            return ClientFileManager.GetFilesInDirectory(rootFolder, searchPattern, true).ToArray();
        }

        Vector3 GetTargetOffsetByZoneOrPath(uint zoneKey, uint pathBlockX, uint pathBlockY)
        {
            if (zoneKey == 0 || !ParentWorldTemplate.XmlWorld.Zones.TryGetValue(zoneKey, out var xmlWorldZone))
                return new Vector3(pathBlockX * 256f, pathBlockY * 256f, 0f);
            return new Vector3(xmlWorldZone.OriginX * 1024f, xmlWorldZone.OriginY * 1024f, 0f);
        }
    }

    private void ClearData()
    {
        // New
        // AreasMissionReader.UsedAreaNames.Clear();
        AreasMissionReaders.Clear();
        NetMissionReaders.Clear();
        VertexMissionReaders.Clear();
    }
}
