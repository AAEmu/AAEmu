using System.Collections.Generic;

namespace AAEmu.Game.Models.Stream
{
    public class UccUploadHandle
    {
        public int ExpectedSize { get; set; }
        public int UploadedSize { get; private set; }
        public List<UccPart> Parts { get; private set; } = new List<UccPart>();
        public bool UploadComplete { get { return UploadedSize >= ExpectedSize; } }
        
        public CustomUcc UploadingUcc { get; set; }

        public void AddPart(UccPart uccPart)
        {
            Parts.Add(uccPart);
            UploadedSize += uccPart.Data.Length;
        }

        public void FinalizeUpload()
        {
            UploadingUcc.Data.Clear();
            foreach (var part in Parts)
            {
                UploadingUcc.Data.AddRange(part.Data);
            }
        }
        
    }
}
