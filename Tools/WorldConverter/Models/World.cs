using System;

namespace AAEmu.WorldConverter.Models
{
    // Примечание. Для запуска созданного кода может потребоваться NET Framework версии 4.5 или более поздней версии и .NET Core или Standard версии 2.0 или более поздней.
    /// <remarks/>
    [Serializable()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(Namespace = "", IsNullable = false)]
    public partial class World
    {
        private WorldZone[] zoneListField;

        private string nameField;

        private ushort resolutionField;

        private byte unitSizeField;

        private byte cellXCountField;

        private byte cellYCountField;

        private byte isInstanceField;

        private byte nextLayerIdField;

        private byte nextSurfaceIdField;

        private byte oceanLevelField;

        private ushort maxTerrainHeightField;

        private byte isReleaseBranchField;

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("Zone", IsNullable = false)]
        public WorldZone[] ZoneList
        {
            get { return this.zoneListField; }
            set { this.zoneListField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string name
        {
            get { return this.nameField; }
            set { this.nameField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort resolution
        {
            get { return this.resolutionField; }
            set { this.resolutionField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte unitSize
        {
            get { return this.unitSizeField; }
            set { this.unitSizeField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte cellXCount
        {
            get { return this.cellXCountField; }
            set { this.cellXCountField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte cellYCount
        {
            get { return this.cellYCountField; }
            set { this.cellYCountField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte isInstance
        {
            get { return this.isInstanceField; }
            set { this.isInstanceField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nextLayerId
        {
            get { return this.nextLayerIdField; }
            set { this.nextLayerIdField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nextSurfaceId
        {
            get { return this.nextSurfaceIdField; }
            set { this.nextSurfaceIdField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte oceanLevel
        {
            get { return this.oceanLevelField; }
            set { this.oceanLevelField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort maxTerrainHeight
        {
            get { return this.maxTerrainHeightField; }
            set { this.maxTerrainHeightField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte isReleaseBranch
        {
            get { return this.isReleaseBranchField; }
            set { this.isReleaseBranchField = value; }
        }
    }

    /// <remarks/>
    [Serializable()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class WorldZone
    {
        private WorldZoneCell[] cellListField;

        private string nameField;

        private ushort idField;

        private byte originXField;

        private byte originYField;

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("cell", IsNullable = false)]
        public WorldZoneCell[] cellList
        {
            get { return this.cellListField; }
            set { this.cellListField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string name
        {
            get { return this.nameField; }
            set { this.nameField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort id
        {
            get { return this.idField; }
            set { this.idField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte originX
        {
            get { return this.originXField; }
            set { this.originXField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte originY
        {
            get { return this.originYField; }
            set { this.originYField = value; }
        }
    }

    /// <remarks/>
    [Serializable()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class WorldZoneCell
    {
        private WorldZoneCellSector[] sectorListField;

        private byte xField;

        private byte yField;

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("sector", IsNullable = false)]
        public WorldZoneCellSector[] sectorList
        {
            get { return this.sectorListField; }
            set { this.sectorListField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get { return this.xField; }
            set { this.xField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get { return this.yField; }
            set { this.yField = value; }
        }
    }

    /// <remarks/>
    [Serializable()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class WorldZoneCellSector
    {
        private byte xField;

        private byte yField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get { return this.xField; }
            set { this.xField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get { return this.yField; }
            set { this.yField = value; }
        }
    }
}
