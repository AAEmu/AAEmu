using System.Text;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Features
{
    public class FeatureSet
    {
        //default fsets
        //private byte[] _fset = { 0x7F, 0x37, 0x34, 0x0F, 0x79, 0x08, 0x7D, 0xCB, 0x37, 0x65, 0x03 };
        private byte[] _fset = { 0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a };
        
        private const int PlayerLevelLimitIndex = 1;
        private const int MateLevelLimitIndex = 8;


        public FeatureSet()
        {
            //You can set some values here or alter the default fset
            
            //Disables Auction Button
            //Set(Feature.hudAuctionButton, false);
            //Enables family invites
            Set(Feature.allowFamilyChanges, true);
            //Disables Dwarf/Warborn character creation (0.5 only)
            Set(Feature.dwarfWarborn, false);

            // Debug convenience flags
            Set(Feature.sensitiveOpeartion, false);
            Set(Feature.secondpass, false);
            Set(Feature.ingameshopSecondpass, false);
            Set(Feature.itemSecure, false);
        }

        private (byte byteIndex, byte bitIndex) GetIndexes(Feature feature)
        {
            byte byteIndex = (byte)((byte)feature / 8);
            byte bitIndex = (byte)((byte)feature % 8);

            return (byteIndex, bitIndex);
        }

        public bool Check(Feature feature)
        {

            (byte byteIndex, byte bitIndex) = GetIndexes(feature);

            try
            {
                return (_fset[byteIndex] & (1 << bitIndex)) != 0;
            }
            catch { throw new System.ArgumentException("Invalid FSet Enum Value. Does not exist in bounds of array."); }
        }

        public bool Set(Feature feature, bool enabled)
        {
            (byte byteIndex, byte bitIndex) = GetIndexes(feature);

            try
            {
                if (enabled)
                    _fset[byteIndex] |= (byte)(1 << bitIndex);
                else
                    _fset[byteIndex] &= (byte)~(1 << bitIndex);
                return true;
            }
            //Catch if feature is out of bounds of array and does not exist.
            catch { return false; }
        }

        //This sets the level cap before you reach ancestral levels.
        public byte PlayerLevelLimit
        {
            get => _fset[PlayerLevelLimitIndex];
            set => _fset[PlayerLevelLimitIndex] = value;
        }

        //Maybe same as player level limit, but for mounts/pets?
        public byte MateLevelLimit
        {
            get => _fset[MateLevelLimitIndex];
            set => _fset[MateLevelLimitIndex] = value;
        }

        public override string ToString()
        {
            StringBuilder hex = new StringBuilder(_fset.Length * 2);
            foreach (byte b in _fset)
                hex.AppendFormat("{0:x2} ", b);
            return hex.ToString();
        }

        public void Write(PacketStream stream)
        {
            stream.Write(_fset, true);
        }
    }
}
