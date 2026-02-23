using Extensions;
using System;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkHelper_744 : CryEngineCore.ChunkHelper
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        HelperType = (HelperType)b.ReadUInt32();
        if (Version == 0x744)  // only has the Position.
        {
            Pos = b.ReadVector3();
        }
        else if (Version == 0x362)   // will probably never see these.
        {
            char[] tmpName = new Char[64];
            tmpName = b.ReadChars(64);
            int stringLength = 0;
            for (int i = 0, j = tmpName.Length; i < j; i++)
            {
                if (tmpName[i] == 0)
                {
                    stringLength = i;
                    break;
                }
            }
            Name = new string(tmpName, 0, stringLength);
            HelperType = (HelperType)b.ReadUInt32();
            Pos = b.ReadVector3();
        }
    }
}
