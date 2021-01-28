﻿using System.Text;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Features
{
    public class FeatureSet
    {
        //default fsets
        // in 1.2 = 11
        //private byte[] _fset = { 0x7F, 0x37, 0x34, 0x0F, 0x79, 0x08, 0x7D, 0xCB, 0x37, 0x65, 0x03 };
        // in 1.2 = 11
        //private byte[] _fset = { 0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a };
        // in 1.2 march = 13
        //private byte[] _fset = { 0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a, 0x00, 0x00 };
        // in 1.7 = 16
        private byte[] _fset =   { 0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a, 0x00, 0x00, 0x00, 0x00, 0x00 };
        // in 3.0.3.0 = 26
        //private byte[] _fset = {0x7F, 0x37, 0x34, 0x0F, 0x79, 0x08, 0x7D, 0xCB, 0x37, 0x65, 0x03, 0xDE, 0xAE, 0x86, 0x3C, 0x0E, 0x02,0xE6, 0x6F, 0xC7, 0xBB, 0x9B, 0x5D, 0x01, 0x00, 0x01};

        private const int PlayerLevelLimitIndex = 1;
        private const int MateLevelLimitIndex = 8;

        public FeatureSet()
        {
            //You can set some values here or alter the default fset

            // Initialization of fset moved to FeatureManager
            /*
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
            */
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
