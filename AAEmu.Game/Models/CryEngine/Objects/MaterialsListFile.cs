using System.Text;
using AAEmu.Game.IO;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class MaterialsListFile(string fileStreamFile)
{
    public List<string> MaterialsList { get; set; } = [];

    public void ReadFile()
    {
        MaterialsList.Clear();
        try
        {
            using var sourceFs = ClientFileManager.GetFileStream(fileStreamFile);
            using var fs = new MemoryStream();
            sourceFs.CopyTo(fs);
            if (fs.Length <= 4)
            {
                // File too small contain data
                return;
            }

            using var br = new BinaryReader(fs);
            var count = br.ReadUInt32();
            for (var i = 0u; i < count; i++)
            {
                _ = br.ReadUInt32(); // Doesn't seem used, always 0x00000000
                var chunkBytes = br.ReadBytes(256);
                var truncatedString = chunkBytes.TakeWhile(b => b != 0).ToArray();
                var materialName = Encoding.UTF8.GetString(truncatedString);
                MaterialsList.Add(materialName);
            }
        }
        catch
        {
            //
        }
    }
}
