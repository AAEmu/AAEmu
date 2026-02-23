using System;
using System.Diagnostics;
using System.IO;
using CgfConverter.CryXmlB;

namespace CgfConverter.Models
{
    // NOTE: Generated code may require at least .NET Framework 4.5 or .NET Core/Standard 2.0.
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(Namespace = "", IsNullable = false)]

    public partial class PrefabsLibrary
    {
        private PrefabsLibraryPrefab[] prefabField;

        private string nameField;

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("Prefab")]
        public PrefabsLibraryPrefab[] Prefab
        {
            get
            {
                return this.prefabField;
            }
            set
            {
                this.prefabField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        public static PrefabsLibrary? FromStream(Stream stream, bool closeAfter = false)
        {
            try
            {
                return CryXmlSerializer.Deserialize<PrefabsLibrary>(stream);
                //return HoloXPLOR.DataForge.CryXmlSerializer.Deserialize<CryEngineCore.Material>(materialfile.FullName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("{0} failed deserialize - {1}", stream, ex.Message);
            }
            finally
            {
                if (closeAfter)
                    stream.Close();                
            }

            return null;
        }

    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefab
    {

        private PrefabsLibraryPrefabObject[] objectsField;

        private string nameField;

        private string idField;

        private string libraryField;

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("Object", IsNullable = false)]
        public PrefabsLibraryPrefabObject[] Objects
        {
            get
            {
                return this.objectsField;
            }
            set
            {
                this.objectsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Id
        {
            get
            {
                return this.idField;
            }
            set
            {
                this.idField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Library
        {
            get
            {
                return this.libraryField;
            }
            set
            {
                this.libraryField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObject
    {

        private PrefabsLibraryPrefabObjectProperties propertiesField;

        private PrefabsLibraryPrefabObjectProperties2 properties2Field;

        private PrefabsLibraryPrefabObjectLink[] entityLinksField;

        private PrefabsLibraryPrefabObjectObject[] objectsField;

        private string typeField;

        private string layerField;

        private string layerGUIDField;

        private string idField;

        private string nameField;

        private string posField;

        private sbyte floorNumberField;

        private string rotateField;

        private uint colorRGBField;

        private byte mergeGeomField;

        private bool mergeGeomFieldSpecified;

        private byte openedField;

        private bool openedFieldSpecified;

        private byte matLayersMaskField;

        private bool matLayersMaskFieldSpecified;

        private string geometryField;

        private byte outdoorOnlyField;

        private bool outdoorOnlyFieldSpecified;

        private byte castShadowField;

        private bool castShadowFieldSpecified;

        private byte lodRatioField;

        private bool lodRatioFieldSpecified;

        private byte viewDistRatioField;

        private bool viewDistRatioFieldSpecified;

        private byte hiddenInGameField;

        private bool hiddenInGameFieldSpecified;

        private byte recvWindField;

        private bool recvWindFieldSpecified;

        private byte renderNearestField;

        private bool renderNearestFieldSpecified;

        private byte noStaticDecalsField;

        private bool noStaticDecalsFieldSpecified;

        private byte createdThroughPoolField;

        private bool createdThroughPoolFieldSpecified;

        private string entityClassField;

        private string scaleField;

        private string prefabField;

        private byte noCollisionField;

        private bool noCollisionFieldSpecified;

        private byte castShadowMapsField;

        private bool castShadowMapsFieldSpecified;

        private byte rainOccluderField;

        private bool rainOccluderFieldSpecified;

        private byte supportSecondVisareaField;

        private bool supportSecondVisareaFieldSpecified;

        private byte hideableField;

        private bool hideableFieldSpecified;

        private byte meshIntegrationTypeField;

        private bool meshIntegrationTypeFieldSpecified;

        private byte notTriangulateField;

        private bool notTriangulateFieldSpecified;

        private sbyte aIRadiusField;

        private bool aIRadiusFieldSpecified;

        private byte noAmnbShadowCasterField;

        private bool noAmnbShadowCasterFieldSpecified;

        private uint rndFlagsField;

        private bool rndFlagsFieldSpecified;

        private string materialField;

        private ushort cubemap_resolutionField;

        private bool cubemap_resolutionFieldSpecified;

        private byte preview_cubemapField;

        private bool preview_cubemapFieldSpecified;

        private byte originAtCornerField;

        private bool originAtCornerFieldSpecified;

        private byte widthField;

        private bool widthFieldSpecified;

        private byte lengthField;

        private bool lengthFieldSpecified;

        private byte heightField;

        private bool heightFieldSpecified;

        private byte disableGIField;

        private bool disableGIFieldSpecified;

        private string boxMinField;

        private string boxMaxField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties Properties
        {
            get
            {
                return this.propertiesField;
            }
            set
            {
                this.propertiesField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2 Properties2
        {
            get
            {
                return this.properties2Field;
            }
            set
            {
                this.properties2Field = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("Link", IsNullable = false)]
        public PrefabsLibraryPrefabObjectLink[] EntityLinks
        {
            get
            {
                return this.entityLinksField;
            }
            set
            {
                this.entityLinksField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("Object", IsNullable = false)]
        public PrefabsLibraryPrefabObjectObject[] Objects
        {
            get
            {
                return this.objectsField;
            }
            set
            {
                this.objectsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Type
        {
            get
            {
                return this.typeField;
            }
            set
            {
                this.typeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Layer
        {
            get
            {
                return this.layerField;
            }
            set
            {
                this.layerField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string LayerGUID
        {
            get
            {
                return this.layerGUIDField;
            }
            set
            {
                this.layerGUIDField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Id
        {
            get
            {
                return this.idField;
            }
            set
            {
                this.idField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Pos
        {
            get
            {
                return this.posField;
            }
            set
            {
                this.posField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte FloorNumber
        {
            get
            {
                return this.floorNumberField;
            }
            set
            {
                this.floorNumberField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Rotate
        {
            get
            {
                return this.rotateField;
            }
            set
            {
                this.rotateField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint ColorRGB
        {
            get
            {
                return this.colorRGBField;
            }
            set
            {
                this.colorRGBField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte MergeGeom
        {
            get
            {
                return this.mergeGeomField;
            }
            set
            {
                this.mergeGeomField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool MergeGeomSpecified
        {
            get
            {
                return this.mergeGeomFieldSpecified;
            }
            set
            {
                this.mergeGeomFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Opened
        {
            get
            {
                return this.openedField;
            }
            set
            {
                this.openedField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool OpenedSpecified
        {
            get
            {
                return this.openedFieldSpecified;
            }
            set
            {
                this.openedFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte MatLayersMask
        {
            get
            {
                return this.matLayersMaskField;
            }
            set
            {
                this.matLayersMaskField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool MatLayersMaskSpecified
        {
            get
            {
                return this.matLayersMaskFieldSpecified;
            }
            set
            {
                this.matLayersMaskFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Geometry
        {
            get
            {
                return this.geometryField;
            }
            set
            {
                this.geometryField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte OutdoorOnly
        {
            get
            {
                return this.outdoorOnlyField;
            }
            set
            {
                this.outdoorOnlyField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool OutdoorOnlySpecified
        {
            get
            {
                return this.outdoorOnlyFieldSpecified;
            }
            set
            {
                this.outdoorOnlyFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte CastShadow
        {
            get
            {
                return this.castShadowField;
            }
            set
            {
                this.castShadowField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool CastShadowSpecified
        {
            get
            {
                return this.castShadowFieldSpecified;
            }
            set
            {
                this.castShadowFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte LodRatio
        {
            get
            {
                return this.lodRatioField;
            }
            set
            {
                this.lodRatioField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool LodRatioSpecified
        {
            get
            {
                return this.lodRatioFieldSpecified;
            }
            set
            {
                this.lodRatioFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte ViewDistRatio
        {
            get
            {
                return this.viewDistRatioField;
            }
            set
            {
                this.viewDistRatioField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool ViewDistRatioSpecified
        {
            get
            {
                return this.viewDistRatioFieldSpecified;
            }
            set
            {
                this.viewDistRatioFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte HiddenInGame
        {
            get
            {
                return this.hiddenInGameField;
            }
            set
            {
                this.hiddenInGameField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool HiddenInGameSpecified
        {
            get
            {
                return this.hiddenInGameFieldSpecified;
            }
            set
            {
                this.hiddenInGameFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte RecvWind
        {
            get
            {
                return this.recvWindField;
            }
            set
            {
                this.recvWindField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool RecvWindSpecified
        {
            get
            {
                return this.recvWindFieldSpecified;
            }
            set
            {
                this.recvWindFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte RenderNearest
        {
            get
            {
                return this.renderNearestField;
            }
            set
            {
                this.renderNearestField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool RenderNearestSpecified
        {
            get
            {
                return this.renderNearestFieldSpecified;
            }
            set
            {
                this.renderNearestFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NoStaticDecals
        {
            get
            {
                return this.noStaticDecalsField;
            }
            set
            {
                this.noStaticDecalsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool NoStaticDecalsSpecified
        {
            get
            {
                return this.noStaticDecalsFieldSpecified;
            }
            set
            {
                this.noStaticDecalsFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte CreatedThroughPool
        {
            get
            {
                return this.createdThroughPoolField;
            }
            set
            {
                this.createdThroughPoolField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool CreatedThroughPoolSpecified
        {
            get
            {
                return this.createdThroughPoolFieldSpecified;
            }
            set
            {
                this.createdThroughPoolFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string EntityClass
        {
            get
            {
                return this.entityClassField;
            }
            set
            {
                this.entityClassField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Scale
        {
            get
            {
                return this.scaleField;
            }
            set
            {
                this.scaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Prefab
        {
            get
            {
                return this.prefabField;
            }
            set
            {
                this.prefabField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NoCollision
        {
            get
            {
                return this.noCollisionField;
            }
            set
            {
                this.noCollisionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool NoCollisionSpecified
        {
            get
            {
                return this.noCollisionFieldSpecified;
            }
            set
            {
                this.noCollisionFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte CastShadowMaps
        {
            get
            {
                return this.castShadowMapsField;
            }
            set
            {
                this.castShadowMapsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool CastShadowMapsSpecified
        {
            get
            {
                return this.castShadowMapsFieldSpecified;
            }
            set
            {
                this.castShadowMapsFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte RainOccluder
        {
            get
            {
                return this.rainOccluderField;
            }
            set
            {
                this.rainOccluderField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool RainOccluderSpecified
        {
            get
            {
                return this.rainOccluderFieldSpecified;
            }
            set
            {
                this.rainOccluderFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte SupportSecondVisarea
        {
            get
            {
                return this.supportSecondVisareaField;
            }
            set
            {
                this.supportSecondVisareaField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool SupportSecondVisareaSpecified
        {
            get
            {
                return this.supportSecondVisareaFieldSpecified;
            }
            set
            {
                this.supportSecondVisareaFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Hideable
        {
            get
            {
                return this.hideableField;
            }
            set
            {
                this.hideableField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool HideableSpecified
        {
            get
            {
                return this.hideableFieldSpecified;
            }
            set
            {
                this.hideableFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte MeshIntegrationType
        {
            get
            {
                return this.meshIntegrationTypeField;
            }
            set
            {
                this.meshIntegrationTypeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool MeshIntegrationTypeSpecified
        {
            get
            {
                return this.meshIntegrationTypeFieldSpecified;
            }
            set
            {
                this.meshIntegrationTypeFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NotTriangulate
        {
            get
            {
                return this.notTriangulateField;
            }
            set
            {
                this.notTriangulateField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool NotTriangulateSpecified
        {
            get
            {
                return this.notTriangulateFieldSpecified;
            }
            set
            {
                this.notTriangulateFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte AIRadius
        {
            get
            {
                return this.aIRadiusField;
            }
            set
            {
                this.aIRadiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool AIRadiusSpecified
        {
            get
            {
                return this.aIRadiusFieldSpecified;
            }
            set
            {
                this.aIRadiusFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NoAmnbShadowCaster
        {
            get
            {
                return this.noAmnbShadowCasterField;
            }
            set
            {
                this.noAmnbShadowCasterField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool NoAmnbShadowCasterSpecified
        {
            get
            {
                return this.noAmnbShadowCasterFieldSpecified;
            }
            set
            {
                this.noAmnbShadowCasterFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint RndFlags
        {
            get
            {
                return this.rndFlagsField;
            }
            set
            {
                this.rndFlagsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool RndFlagsSpecified
        {
            get
            {
                return this.rndFlagsFieldSpecified;
            }
            set
            {
                this.rndFlagsFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Material
        {
            get
            {
                return this.materialField;
            }
            set
            {
                this.materialField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort cubemap_resolution
        {
            get
            {
                return this.cubemap_resolutionField;
            }
            set
            {
                this.cubemap_resolutionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool cubemap_resolutionSpecified
        {
            get
            {
                return this.cubemap_resolutionFieldSpecified;
            }
            set
            {
                this.cubemap_resolutionFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte preview_cubemap
        {
            get
            {
                return this.preview_cubemapField;
            }
            set
            {
                this.preview_cubemapField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool preview_cubemapSpecified
        {
            get
            {
                return this.preview_cubemapFieldSpecified;
            }
            set
            {
                this.preview_cubemapFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte OriginAtCorner
        {
            get
            {
                return this.originAtCornerField;
            }
            set
            {
                this.originAtCornerField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool OriginAtCornerSpecified
        {
            get
            {
                return this.originAtCornerFieldSpecified;
            }
            set
            {
                this.originAtCornerFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Width
        {
            get
            {
                return this.widthField;
            }
            set
            {
                this.widthField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool WidthSpecified
        {
            get
            {
                return this.widthFieldSpecified;
            }
            set
            {
                this.widthFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Length
        {
            get
            {
                return this.lengthField;
            }
            set
            {
                this.lengthField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool LengthSpecified
        {
            get
            {
                return this.lengthFieldSpecified;
            }
            set
            {
                this.lengthFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Height
        {
            get
            {
                return this.heightField;
            }
            set
            {
                this.heightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool HeightSpecified
        {
            get
            {
                return this.heightFieldSpecified;
            }
            set
            {
                this.heightFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte DisableGI
        {
            get
            {
                return this.disableGIField;
            }
            set
            {
                this.disableGIField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DisableGISpecified
        {
            get
            {
                return this.disableGIFieldSpecified;
            }
            set
            {
                this.disableGIFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string BoxMin
        {
            get
            {
                return this.boxMinField;
            }
            set
            {
                this.boxMinField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string BoxMax
        {
            get
            {
                return this.boxMaxField;
            }
            set
            {
                this.boxMaxField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties
    {

        private PrefabsLibraryPrefabObjectPropertiesSize sizeField;

        private PrefabsLibraryPrefabObjectPropertiesBreakage breakageField;

        private PrefabsLibraryPrefabObjectPropertiesDamage damageField;

        private PrefabsLibraryPrefabObjectPropertiesDamageMultipliers damageMultipliersField;

        private PrefabsLibraryPrefabObjectPropertiesExplosion explosionField;

        private PrefabsLibraryPrefabObjectPropertiesModel modelField;

        private PrefabsLibraryPrefabObjectPropertiesPhysics physicsField;

        private PrefabsLibraryPrefabObjectPropertiesSounds soundsField;

        private PrefabsLibraryPrefabObjectPropertiesVulnerability vulnerabilityField;

        private PrefabsLibraryPrefabObjectPropertiesColor colorField;

        private PrefabsLibraryPrefabObjectPropertiesOptions optionsField;

        private PrefabsLibraryPrefabObjectPropertiesOptionsAdvanced optionsAdvancedField;

        private PrefabsLibraryPrefabObjectPropertiesProjector projectorField;

        private PrefabsLibraryPrefabObjectPropertiesStyle styleField;

        private PrefabsLibraryPrefabObjectPropertiesHealth healthField;

        private PrefabsLibraryPrefabObjectPropertiesInterest interestField;

        private string esFactionField;

        private byte bActiveField;

        private bool bActiveFieldSpecified;

        private decimal radiusField;

        private bool radiusFieldSpecified;

        private sbyte _nVersionField;

        private bool _nVersionFieldSpecified;

        private byte bPickableField;

        private bool bPickableFieldSpecified;

        private byte bUsableField;

        private bool bUsableFieldSpecified;

        private string useTextField;

        private string color_ColorField;

        private byte densityOffsetField;

        private bool densityOffsetFieldSpecified;

        private byte fallOffDirLatiField;

        private bool fallOffDirLatiFieldSpecified;

        private byte fallOffDirLongField;

        private bool fallOffDirLongFieldSpecified;

        private byte fallOffScaleField;

        private bool fallOffScaleFieldSpecified;

        private byte fallOffShiftField;

        private bool fallOffShiftFieldSpecified;

        private decimal globalDensityField;

        private bool globalDensityFieldSpecified;

        private byte fHDRDynamicField;

        private bool fHDRDynamicFieldSpecified;

        private byte nearCutoffField;

        private bool nearCutoffFieldSpecified;

        private byte softEdgesField;

        private bool softEdgesFieldSpecified;

        private byte bUseGlobalFogColorField;

        private bool bUseGlobalFogColorFieldSpecified;

        private byte volumeTypeField;

        private bool volumeTypeFieldSpecified;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesSize Size
        {
            get
            {
                return this.sizeField;
            }
            set
            {
                this.sizeField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesBreakage Breakage
        {
            get
            {
                return this.breakageField;
            }
            set
            {
                this.breakageField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesDamage Damage
        {
            get
            {
                return this.damageField;
            }
            set
            {
                this.damageField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesDamageMultipliers DamageMultipliers
        {
            get
            {
                return this.damageMultipliersField;
            }
            set
            {
                this.damageMultipliersField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosion Explosion
        {
            get
            {
                return this.explosionField;
            }
            set
            {
                this.explosionField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesModel Model
        {
            get
            {
                return this.modelField;
            }
            set
            {
                this.modelField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesPhysics Physics
        {
            get
            {
                return this.physicsField;
            }
            set
            {
                this.physicsField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesSounds Sounds
        {
            get
            {
                return this.soundsField;
            }
            set
            {
                this.soundsField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesVulnerability Vulnerability
        {
            get
            {
                return this.vulnerabilityField;
            }
            set
            {
                this.vulnerabilityField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesColor Color
        {
            get
            {
                return this.colorField;
            }
            set
            {
                this.colorField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesOptions Options
        {
            get
            {
                return this.optionsField;
            }
            set
            {
                this.optionsField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesOptionsAdvanced OptionsAdvanced
        {
            get
            {
                return this.optionsAdvancedField;
            }
            set
            {
                this.optionsAdvancedField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesProjector Projector
        {
            get
            {
                return this.projectorField;
            }
            set
            {
                this.projectorField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesStyle Style
        {
            get
            {
                return this.styleField;
            }
            set
            {
                this.styleField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesHealth Health
        {
            get
            {
                return this.healthField;
            }
            set
            {
                this.healthField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesInterest Interest
        {
            get
            {
                return this.interestField;
            }
            set
            {
                this.interestField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string esFaction
        {
            get
            {
                return this.esFactionField;
            }
            set
            {
                this.esFactionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bActive
        {
            get
            {
                return this.bActiveField;
            }
            set
            {
                this.bActiveField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bActiveSpecified
        {
            get
            {
                return this.bActiveFieldSpecified;
            }
            set
            {
                this.bActiveFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal Radius
        {
            get
            {
                return this.radiusField;
            }
            set
            {
                this.radiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool RadiusSpecified
        {
            get
            {
                return this.radiusFieldSpecified;
            }
            set
            {
                this.radiusFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte _nVersion
        {
            get
            {
                return this._nVersionField;
            }
            set
            {
                this._nVersionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool _nVersionSpecified
        {
            get
            {
                return this._nVersionFieldSpecified;
            }
            set
            {
                this._nVersionFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bPickable
        {
            get
            {
                return this.bPickableField;
            }
            set
            {
                this.bPickableField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bPickableSpecified
        {
            get
            {
                return this.bPickableFieldSpecified;
            }
            set
            {
                this.bPickableFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bUsable
        {
            get
            {
                return this.bUsableField;
            }
            set
            {
                this.bUsableField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bUsableSpecified
        {
            get
            {
                return this.bUsableFieldSpecified;
            }
            set
            {
                this.bUsableFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string UseText
        {
            get
            {
                return this.useTextField;
            }
            set
            {
                this.useTextField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string color_Color
        {
            get
            {
                return this.color_ColorField;
            }
            set
            {
                this.color_ColorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte DensityOffset
        {
            get
            {
                return this.densityOffsetField;
            }
            set
            {
                this.densityOffsetField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DensityOffsetSpecified
        {
            get
            {
                return this.densityOffsetFieldSpecified;
            }
            set
            {
                this.densityOffsetFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte FallOffDirLati
        {
            get
            {
                return this.fallOffDirLatiField;
            }
            set
            {
                this.fallOffDirLatiField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool FallOffDirLatiSpecified
        {
            get
            {
                return this.fallOffDirLatiFieldSpecified;
            }
            set
            {
                this.fallOffDirLatiFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte FallOffDirLong
        {
            get
            {
                return this.fallOffDirLongField;
            }
            set
            {
                this.fallOffDirLongField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool FallOffDirLongSpecified
        {
            get
            {
                return this.fallOffDirLongFieldSpecified;
            }
            set
            {
                this.fallOffDirLongFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte FallOffScale
        {
            get
            {
                return this.fallOffScaleField;
            }
            set
            {
                this.fallOffScaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool FallOffScaleSpecified
        {
            get
            {
                return this.fallOffScaleFieldSpecified;
            }
            set
            {
                this.fallOffScaleFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte FallOffShift
        {
            get
            {
                return this.fallOffShiftField;
            }
            set
            {
                this.fallOffShiftField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool FallOffShiftSpecified
        {
            get
            {
                return this.fallOffShiftFieldSpecified;
            }
            set
            {
                this.fallOffShiftFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal GlobalDensity
        {
            get
            {
                return this.globalDensityField;
            }
            set
            {
                this.globalDensityField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool GlobalDensitySpecified
        {
            get
            {
                return this.globalDensityFieldSpecified;
            }
            set
            {
                this.globalDensityFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fHDRDynamic
        {
            get
            {
                return this.fHDRDynamicField;
            }
            set
            {
                this.fHDRDynamicField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool fHDRDynamicSpecified
        {
            get
            {
                return this.fHDRDynamicFieldSpecified;
            }
            set
            {
                this.fHDRDynamicFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NearCutoff
        {
            get
            {
                return this.nearCutoffField;
            }
            set
            {
                this.nearCutoffField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool NearCutoffSpecified
        {
            get
            {
                return this.nearCutoffFieldSpecified;
            }
            set
            {
                this.nearCutoffFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte SoftEdges
        {
            get
            {
                return this.softEdgesField;
            }
            set
            {
                this.softEdgesField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool SoftEdgesSpecified
        {
            get
            {
                return this.softEdgesFieldSpecified;
            }
            set
            {
                this.softEdgesFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bUseGlobalFogColor
        {
            get
            {
                return this.bUseGlobalFogColorField;
            }
            set
            {
                this.bUseGlobalFogColorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bUseGlobalFogColorSpecified
        {
            get
            {
                return this.bUseGlobalFogColorFieldSpecified;
            }
            set
            {
                this.bUseGlobalFogColorFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte VolumeType
        {
            get
            {
                return this.volumeTypeField;
            }
            set
            {
                this.volumeTypeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool VolumeTypeSpecified
        {
            get
            {
                return this.volumeTypeFieldSpecified;
            }
            set
            {
                this.volumeTypeFieldSpecified = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesSize
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesBreakage
    {

        private byte fExplodeImpulseField;

        private byte fLifeTimeField;

        private byte bSurfaceEffectsField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fExplodeImpulse
        {
            get
            {
                return this.fExplodeImpulseField;
            }
            set
            {
                this.fExplodeImpulseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fLifeTime
        {
            get
            {
                return this.fLifeTimeField;
            }
            set
            {
                this.fLifeTimeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bSurfaceEffects
        {
            get
            {
                return this.bSurfaceEffectsField;
            }
            set
            {
                this.bSurfaceEffectsField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesDamage
    {

        private byte fDamageTresholdField;

        private byte bExplodeField;

        private byte fHealthField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fDamageTreshold
        {
            get
            {
                return this.fDamageTresholdField;
            }
            set
            {
                this.fDamageTresholdField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bExplode
        {
            get
            {
                return this.bExplodeField;
            }
            set
            {
                this.bExplodeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fHealth
        {
            get
            {
                return this.fHealthField;
            }
            set
            {
                this.fHealthField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesDamageMultipliers
    {

        private byte fBulletField;

        private byte fCollisionField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fBullet
        {
            get
            {
                return this.fBulletField;
            }
            set
            {
                this.fBulletField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCollision
        {
            get
            {
                return this.fCollisionField;
            }
            set
            {
                this.fCollisionField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosion
    {

        private PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffect delayEffectField;

        private PrefabsLibraryPrefabObjectPropertiesExplosionDirection directionField;

        private PrefabsLibraryPrefabObjectPropertiesExplosionVOffset vOffsetField;

        private ushort damageField;

        private string decalField;

        private byte delayField;

        private string effectField;

        private byte effectScaleField;

        private byte holeSizeField;

        private decimal minPhysRadiusField;

        private byte minRadiusField;

        private byte physRadiusField;

        private ushort pressureField;

        private byte radiusField;

        private byte terrainHoleSizeField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffect DelayEffect
        {
            get
            {
                return this.delayEffectField;
            }
            set
            {
                this.delayEffectField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosionDirection Direction
        {
            get
            {
                return this.directionField;
            }
            set
            {
                this.directionField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosionVOffset vOffset
        {
            get
            {
                return this.vOffsetField;
            }
            set
            {
                this.vOffsetField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort Damage
        {
            get
            {
                return this.damageField;
            }
            set
            {
                this.damageField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Decal
        {
            get
            {
                return this.decalField;
            }
            set
            {
                this.decalField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Delay
        {
            get
            {
                return this.delayField;
            }
            set
            {
                this.delayField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Effect
        {
            get
            {
                return this.effectField;
            }
            set
            {
                this.effectField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte EffectScale
        {
            get
            {
                return this.effectScaleField;
            }
            set
            {
                this.effectScaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte HoleSize
        {
            get
            {
                return this.holeSizeField;
            }
            set
            {
                this.holeSizeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal MinPhysRadius
        {
            get
            {
                return this.minPhysRadiusField;
            }
            set
            {
                this.minPhysRadiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte MinRadius
        {
            get
            {
                return this.minRadiusField;
            }
            set
            {
                this.minRadiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte PhysRadius
        {
            get
            {
                return this.physRadiusField;
            }
            set
            {
                this.physRadiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort Pressure
        {
            get
            {
                return this.pressureField;
            }
            set
            {
                this.pressureField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Radius
        {
            get
            {
                return this.radiusField;
            }
            set
            {
                this.radiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte TerrainHoleSize
        {
            get
            {
                return this.terrainHoleSizeField;
            }
            set
            {
                this.terrainHoleSizeField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffect
    {

        private PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectParams paramsField;

        private PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectVOffset vOffsetField;

        private PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectVRotation vRotationField;

        private string effectField;

        private byte bHasDelayEffectField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectParams Params
        {
            get
            {
                return this.paramsField;
            }
            set
            {
                this.paramsField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectVOffset vOffset
        {
            get
            {
                return this.vOffsetField;
            }
            set
            {
                this.vOffsetField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectVRotation vRotation
        {
            get
            {
                return this.vRotationField;
            }
            set
            {
                this.vRotationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Effect
        {
            get
            {
                return this.effectField;
            }
            set
            {
                this.effectField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bHasDelayEffect
        {
            get
            {
                return this.bHasDelayEffectField;
            }
            set
            {
                this.bHasDelayEffectField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectParams
    {

        private string attachFormField;

        private string attachTypeField;

        private byte bCountPerUnitField;

        private byte countScaleField;

        private byte bPrimeField;

        private byte scaleField;

        private byte bSizePerUnitField;

        private byte spawnPeriodField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string AttachForm
        {
            get
            {
                return this.attachFormField;
            }
            set
            {
                this.attachFormField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string AttachType
        {
            get
            {
                return this.attachTypeField;
            }
            set
            {
                this.attachTypeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bCountPerUnit
        {
            get
            {
                return this.bCountPerUnitField;
            }
            set
            {
                this.bCountPerUnitField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte CountScale
        {
            get
            {
                return this.countScaleField;
            }
            set
            {
                this.countScaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bPrime
        {
            get
            {
                return this.bPrimeField;
            }
            set
            {
                this.bPrimeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Scale
        {
            get
            {
                return this.scaleField;
            }
            set
            {
                this.scaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bSizePerUnit
        {
            get
            {
                return this.bSizePerUnitField;
            }
            set
            {
                this.bSizePerUnitField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte SpawnPeriod
        {
            get
            {
                return this.spawnPeriodField;
            }
            set
            {
                this.spawnPeriodField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectVOffset
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosionDelayEffectVRotation
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosionDirection
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesExplosionVOffset
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesModel
    {

        private string object_ModelField;

        private string object_ModelDestroyedField;

        private string subObject_NameField;

        private string subObject_NameDestroyedField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string object_Model
        {
            get
            {
                return this.object_ModelField;
            }
            set
            {
                this.object_ModelField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string object_ModelDestroyed
        {
            get
            {
                return this.object_ModelDestroyedField;
            }
            set
            {
                this.object_ModelDestroyedField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string SubObject_Name
        {
            get
            {
                return this.subObject_NameField;
            }
            set
            {
                this.subObject_NameField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string SubObject_NameDestroyed
        {
            get
            {
                return this.subObject_NameDestroyedField;
            }
            set
            {
                this.subObject_NameDestroyedField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesPhysics
    {

        private PrefabsLibraryPrefabObjectPropertiesPhysicsSimulation simulationField;

        private byte bActivateOnDamageField;

        private byte bCanBreakOthersField;

        private sbyte densityField;

        private sbyte massField;

        private byte bPushableByPlayersField;

        private byte bRigidBodyField;

        private byte bRigidBodyActiveField;

        private byte bRigidBodyAfterDeathField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesPhysicsSimulation Simulation
        {
            get
            {
                return this.simulationField;
            }
            set
            {
                this.simulationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bActivateOnDamage
        {
            get
            {
                return this.bActivateOnDamageField;
            }
            set
            {
                this.bActivateOnDamageField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bCanBreakOthers
        {
            get
            {
                return this.bCanBreakOthersField;
            }
            set
            {
                this.bCanBreakOthersField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte Density
        {
            get
            {
                return this.densityField;
            }
            set
            {
                this.densityField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte Mass
        {
            get
            {
                return this.massField;
            }
            set
            {
                this.massField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bPushableByPlayers
        {
            get
            {
                return this.bPushableByPlayersField;
            }
            set
            {
                this.bPushableByPlayersField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bRigidBody
        {
            get
            {
                return this.bRigidBodyField;
            }
            set
            {
                this.bRigidBodyField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bRigidBodyActive
        {
            get
            {
                return this.bRigidBodyActiveField;
            }
            set
            {
                this.bRigidBodyActiveField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bRigidBodyAfterDeath
        {
            get
            {
                return this.bRigidBodyAfterDeathField;
            }
            set
            {
                this.bRigidBodyAfterDeathField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesPhysicsSimulation
    {

        private byte dampingField;

        private decimal max_time_stepField;

        private decimal sleep_speedField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte damping
        {
            get
            {
                return this.dampingField;
            }
            set
            {
                this.dampingField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal max_time_step
        {
            get
            {
                return this.max_time_stepField;
            }
            set
            {
                this.max_time_stepField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal sleep_speed
        {
            get
            {
                return this.sleep_speedField;
            }
            set
            {
                this.sleep_speedField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesSounds
    {

        private byte fAISoundRadiusField;

        private string sound_AliveField;

        private string sound_DeadField;

        private string sound_DyingField;

        private byte bStopSoundsOnDestroyedField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fAISoundRadius
        {
            get
            {
                return this.fAISoundRadiusField;
            }
            set
            {
                this.fAISoundRadiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string sound_Alive
        {
            get
            {
                return this.sound_AliveField;
            }
            set
            {
                this.sound_AliveField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string sound_Dead
        {
            get
            {
                return this.sound_DeadField;
            }
            set
            {
                this.sound_DeadField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string sound_Dying
        {
            get
            {
                return this.sound_DyingField;
            }
            set
            {
                this.sound_DyingField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bStopSoundsOnDestroyed
        {
            get
            {
                return this.bStopSoundsOnDestroyedField;
            }
            set
            {
                this.bStopSoundsOnDestroyedField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesVulnerability
    {

        private byte bBulletField;

        private byte bCollisionField;

        private byte bExplosionField;

        private byte bMeleeField;

        private byte bOtherField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bBullet
        {
            get
            {
                return this.bBulletField;
            }
            set
            {
                this.bBulletField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bCollision
        {
            get
            {
                return this.bCollisionField;
            }
            set
            {
                this.bCollisionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bExplosion
        {
            get
            {
                return this.bExplosionField;
            }
            set
            {
                this.bExplosionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bMelee
        {
            get
            {
                return this.bMeleeField;
            }
            set
            {
                this.bMeleeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bOther
        {
            get
            {
                return this.bOtherField;
            }
            set
            {
                this.bOtherField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesColor
    {

        private string clrDiffuseField;

        private decimal fDiffuseMultiplierField;

        private decimal fHDRDynamicField;

        private decimal fSpecularMultiplierField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string clrDiffuse
        {
            get
            {
                return this.clrDiffuseField;
            }
            set
            {
                this.clrDiffuseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal fDiffuseMultiplier
        {
            get
            {
                return this.fDiffuseMultiplierField;
            }
            set
            {
                this.fDiffuseMultiplierField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal fHDRDynamic
        {
            get
            {
                return this.fHDRDynamicField;
            }
            set
            {
                this.fHDRDynamicField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal fSpecularMultiplier
        {
            get
            {
                return this.fSpecularMultiplierField;
            }
            set
            {
                this.fSpecularMultiplierField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesOptions
    {

        private byte bAffectsThisAreaOnlyField;

        private byte bAmbientLightField;

        private bool bAmbientLightFieldSpecified;

        private byte bCastShadowField;

        private bool bCastShadowFieldSpecified;

        private byte nCastShadowsField;

        private bool nCastShadowsFieldSpecified;

        private string file_deferred_clip_geomField;

        private string texture_deferred_cubemapField;

        private byte bDeferredClipBoundsField;

        private byte bDeferredLightField;

        private byte bFakeLightField;

        private bool bFakeLightFieldSpecified;

        private byte bIgnoresVisAreasField;

        private byte bIrradianceVolumesField;

        private bool bIrradianceVolumesFieldSpecified;

        private byte bNegativeLightField;

        private bool bNegativeLightFieldSpecified;

        private byte nPostEffectField;

        private bool nPostEffectFieldSpecified;

        private string _texture_deferred_cubemapField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bAffectsThisAreaOnly
        {
            get
            {
                return this.bAffectsThisAreaOnlyField;
            }
            set
            {
                this.bAffectsThisAreaOnlyField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bAmbientLight
        {
            get
            {
                return this.bAmbientLightField;
            }
            set
            {
                this.bAmbientLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bAmbientLightSpecified
        {
            get
            {
                return this.bAmbientLightFieldSpecified;
            }
            set
            {
                this.bAmbientLightFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bCastShadow
        {
            get
            {
                return this.bCastShadowField;
            }
            set
            {
                this.bCastShadowField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bCastShadowSpecified
        {
            get
            {
                return this.bCastShadowFieldSpecified;
            }
            set
            {
                this.bCastShadowFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nCastShadows
        {
            get
            {
                return this.nCastShadowsField;
            }
            set
            {
                this.nCastShadowsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool nCastShadowsSpecified
        {
            get
            {
                return this.nCastShadowsFieldSpecified;
            }
            set
            {
                this.nCastShadowsFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string file_deferred_clip_geom
        {
            get
            {
                return this.file_deferred_clip_geomField;
            }
            set
            {
                this.file_deferred_clip_geomField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string texture_deferred_cubemap
        {
            get
            {
                return this.texture_deferred_cubemapField;
            }
            set
            {
                this.texture_deferred_cubemapField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bDeferredClipBounds
        {
            get
            {
                return this.bDeferredClipBoundsField;
            }
            set
            {
                this.bDeferredClipBoundsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bDeferredLight
        {
            get
            {
                return this.bDeferredLightField;
            }
            set
            {
                this.bDeferredLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bFakeLight
        {
            get
            {
                return this.bFakeLightField;
            }
            set
            {
                this.bFakeLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bFakeLightSpecified
        {
            get
            {
                return this.bFakeLightFieldSpecified;
            }
            set
            {
                this.bFakeLightFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bIgnoresVisAreas
        {
            get
            {
                return this.bIgnoresVisAreasField;
            }
            set
            {
                this.bIgnoresVisAreasField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bIrradianceVolumes
        {
            get
            {
                return this.bIrradianceVolumesField;
            }
            set
            {
                this.bIrradianceVolumesField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bIrradianceVolumesSpecified
        {
            get
            {
                return this.bIrradianceVolumesFieldSpecified;
            }
            set
            {
                this.bIrradianceVolumesFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bNegativeLight
        {
            get
            {
                return this.bNegativeLightField;
            }
            set
            {
                this.bNegativeLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool bNegativeLightSpecified
        {
            get
            {
                return this.bNegativeLightFieldSpecified;
            }
            set
            {
                this.bNegativeLightFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nPostEffect
        {
            get
            {
                return this.nPostEffectField;
            }
            set
            {
                this.nPostEffectField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool nPostEffectSpecified
        {
            get
            {
                return this.nPostEffectFieldSpecified;
            }
            set
            {
                this.nPostEffectFieldSpecified = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string _texture_deferred_cubemap
        {
            get
            {
                return this._texture_deferred_cubemapField;
            }
            set
            {
                this._texture_deferred_cubemapField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesOptionsAdvanced
    {

        private string texture_deferred_cubemapField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string texture_deferred_cubemap
        {
            get
            {
                return this.texture_deferred_cubemapField;
            }
            set
            {
                this.texture_deferred_cubemapField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesProjector
    {

        private byte bProjectInAllDirsField;

        private byte fProjectorFovField;

        private byte fProjectorNearPlaneField;

        private string texture_TextureField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bProjectInAllDirs
        {
            get
            {
                return this.bProjectInAllDirsField;
            }
            set
            {
                this.bProjectInAllDirsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fProjectorFov
        {
            get
            {
                return this.fProjectorFovField;
            }
            set
            {
                this.fProjectorFovField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fProjectorNearPlane
        {
            get
            {
                return this.fProjectorNearPlaneField;
            }
            set
            {
                this.fProjectorNearPlaneField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string texture_Texture
        {
            get
            {
                return this.texture_TextureField;
            }
            set
            {
                this.texture_TextureField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesStyle
    {

        private PrefabsLibraryPrefabObjectPropertiesStyleRotation rotationField;

        private byte nAnimationPhaseField;

        private decimal fAnimationSpeedField;

        private string texture_AttenuationMapField;

        private byte fCoronaDistIntensityFactorField;

        private byte fCoronaDistSizeFactorField;

        private byte fCoronaScaleField;

        private byte nLightStyleField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesStyleRotation Rotation
        {
            get
            {
                return this.rotationField;
            }
            set
            {
                this.rotationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nAnimationPhase
        {
            get
            {
                return this.nAnimationPhaseField;
            }
            set
            {
                this.nAnimationPhaseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal fAnimationSpeed
        {
            get
            {
                return this.fAnimationSpeedField;
            }
            set
            {
                this.fAnimationSpeedField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string texture_AttenuationMap
        {
            get
            {
                return this.texture_AttenuationMapField;
            }
            set
            {
                this.texture_AttenuationMapField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaDistIntensityFactor
        {
            get
            {
                return this.fCoronaDistIntensityFactorField;
            }
            set
            {
                this.fCoronaDistIntensityFactorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaDistSizeFactor
        {
            get
            {
                return this.fCoronaDistSizeFactorField;
            }
            set
            {
                this.fCoronaDistSizeFactorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaScale
        {
            get
            {
                return this.fCoronaScaleField;
            }
            set
            {
                this.fCoronaScaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nLightStyle
        {
            get
            {
                return this.nLightStyleField;
            }
            set
            {
                this.nLightStyleField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesStyleRotation
    {

        private byte xField;

        private byte yField;

        private decimal zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesHealth
    {

        private byte bInvulnerableField;

        private ushort maxHealthField;

        private byte bOnlyEnemyFireField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bInvulnerable
        {
            get
            {
                return this.bInvulnerableField;
            }
            set
            {
                this.bInvulnerableField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public ushort MaxHealth
        {
            get
            {
                return this.maxHealthField;
            }
            set
            {
                this.maxHealthField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bOnlyEnemyFire
        {
            get
            {
                return this.bOnlyEnemyFireField;
            }
            set
            {
                this.bOnlyEnemyFireField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesInterest
    {

        private PrefabsLibraryPrefabObjectPropertiesInterestVOffset vOffsetField;

        private string soaction_ActionField;

        private byte bInterestingField;

        private byte interestLevelField;

        private byte pauseField;

        private byte radiusField;

        private byte bSharedField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectPropertiesInterestVOffset vOffset
        {
            get
            {
                return this.vOffsetField;
            }
            set
            {
                this.vOffsetField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string soaction_Action
        {
            get
            {
                return this.soaction_ActionField;
            }
            set
            {
                this.soaction_ActionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bInteresting
        {
            get
            {
                return this.bInterestingField;
            }
            set
            {
                this.bInterestingField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte InterestLevel
        {
            get
            {
                return this.interestLevelField;
            }
            set
            {
                this.interestLevelField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Pause
        {
            get
            {
                return this.pauseField;
            }
            set
            {
                this.pauseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Radius
        {
            get
            {
                return this.radiusField;
            }
            set
            {
                this.radiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bShared
        {
            get
            {
                return this.bSharedField;
            }
            set
            {
                this.bSharedField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectPropertiesInterestVOffset
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2
    {

        private PrefabsLibraryPrefabObjectProperties2LightProperties_Base lightProperties_BaseField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_Destroyed lightProperties_DestroyedField;

        private byte bTurnedOnField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_Base LightProperties_Base
        {
            get
            {
                return this.lightProperties_BaseField;
            }
            set
            {
                this.lightProperties_BaseField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_Destroyed LightProperties_Destroyed
        {
            get
            {
                return this.lightProperties_DestroyedField;
            }
            set
            {
                this.lightProperties_DestroyedField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bTurnedOn
        {
            get
            {
                return this.bTurnedOnField;
            }
            set
            {
                this.bTurnedOnField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_Base
    {

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseColor colorField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseOptions optionsField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseProjector projectorField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseStyle styleField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseTest testField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseVDirection vDirectionField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseVOffset vOffsetField;

        private byte radiusField;

        private byte bUseThisLightField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseColor Color
        {
            get
            {
                return this.colorField;
            }
            set
            {
                this.colorField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseOptions Options
        {
            get
            {
                return this.optionsField;
            }
            set
            {
                this.optionsField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseProjector Projector
        {
            get
            {
                return this.projectorField;
            }
            set
            {
                this.projectorField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseStyle Style
        {
            get
            {
                return this.styleField;
            }
            set
            {
                this.styleField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseTest Test
        {
            get
            {
                return this.testField;
            }
            set
            {
                this.testField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseVDirection vDirection
        {
            get
            {
                return this.vDirectionField;
            }
            set
            {
                this.vDirectionField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseVOffset vOffset
        {
            get
            {
                return this.vOffsetField;
            }
            set
            {
                this.vOffsetField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Radius
        {
            get
            {
                return this.radiusField;
            }
            set
            {
                this.radiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bUseThisLight
        {
            get
            {
                return this.bUseThisLightField;
            }
            set
            {
                this.bUseThisLightField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseColor
    {

        private string clrDiffuseField;

        private byte fDiffuseMultiplierField;

        private byte fHDRDynamicField;

        private byte fSpecularMultiplierField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string clrDiffuse
        {
            get
            {
                return this.clrDiffuseField;
            }
            set
            {
                this.clrDiffuseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fDiffuseMultiplier
        {
            get
            {
                return this.fDiffuseMultiplierField;
            }
            set
            {
                this.fDiffuseMultiplierField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fHDRDynamic
        {
            get
            {
                return this.fHDRDynamicField;
            }
            set
            {
                this.fHDRDynamicField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fSpecularMultiplier
        {
            get
            {
                return this.fSpecularMultiplierField;
            }
            set
            {
                this.fSpecularMultiplierField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseOptions
    {

        private byte bAffectsThisAreaOnlyField;

        private byte nCastShadowsField;

        private byte bDeferredLightField;

        private byte bFakeLightField;

        private byte nGlowSubmatIdField;

        private byte bIgnoreGeomCasterField;

        private byte bUsedInRealTimeField;

        private byte nViewDistRatioField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bAffectsThisAreaOnly
        {
            get
            {
                return this.bAffectsThisAreaOnlyField;
            }
            set
            {
                this.bAffectsThisAreaOnlyField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nCastShadows
        {
            get
            {
                return this.nCastShadowsField;
            }
            set
            {
                this.nCastShadowsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bDeferredLight
        {
            get
            {
                return this.bDeferredLightField;
            }
            set
            {
                this.bDeferredLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bFakeLight
        {
            get
            {
                return this.bFakeLightField;
            }
            set
            {
                this.bFakeLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nGlowSubmatId
        {
            get
            {
                return this.nGlowSubmatIdField;
            }
            set
            {
                this.nGlowSubmatIdField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bIgnoreGeomCaster
        {
            get
            {
                return this.bIgnoreGeomCasterField;
            }
            set
            {
                this.bIgnoreGeomCasterField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bUsedInRealTime
        {
            get
            {
                return this.bUsedInRealTimeField;
            }
            set
            {
                this.bUsedInRealTimeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nViewDistRatio
        {
            get
            {
                return this.nViewDistRatioField;
            }
            set
            {
                this.nViewDistRatioField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseProjector
    {

        private byte bProjectInAllDirsField;

        private byte fProjectorFovField;

        private byte fProjectorNearPlaneField;

        private string texture_TextureField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bProjectInAllDirs
        {
            get
            {
                return this.bProjectInAllDirsField;
            }
            set
            {
                this.bProjectInAllDirsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fProjectorFov
        {
            get
            {
                return this.fProjectorFovField;
            }
            set
            {
                this.fProjectorFovField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fProjectorNearPlane
        {
            get
            {
                return this.fProjectorNearPlaneField;
            }
            set
            {
                this.fProjectorNearPlaneField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string texture_Texture
        {
            get
            {
                return this.texture_TextureField;
            }
            set
            {
                this.texture_TextureField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseStyle
    {

        private PrefabsLibraryPrefabObjectProperties2LightProperties_BaseStyleRotation rotationField;

        private byte nAnimationPhaseField;

        private decimal fAnimationSpeedField;

        private byte fCoronaDistIntensityFactorField;

        private byte fCoronaDistSizeFactorField;

        private byte fCoronaScaleField;

        private byte nLightStyleField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_BaseStyleRotation Rotation
        {
            get
            {
                return this.rotationField;
            }
            set
            {
                this.rotationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nAnimationPhase
        {
            get
            {
                return this.nAnimationPhaseField;
            }
            set
            {
                this.nAnimationPhaseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public decimal fAnimationSpeed
        {
            get
            {
                return this.fAnimationSpeedField;
            }
            set
            {
                this.fAnimationSpeedField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaDistIntensityFactor
        {
            get
            {
                return this.fCoronaDistIntensityFactorField;
            }
            set
            {
                this.fCoronaDistIntensityFactorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaDistSizeFactor
        {
            get
            {
                return this.fCoronaDistSizeFactorField;
            }
            set
            {
                this.fCoronaDistSizeFactorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaScale
        {
            get
            {
                return this.fCoronaScaleField;
            }
            set
            {
                this.fCoronaScaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nLightStyle
        {
            get
            {
                return this.nLightStyleField;
            }
            set
            {
                this.nLightStyleField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseStyleRotation
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseTest
    {

        private byte bFillLightField;

        private byte bNegativeLightField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bFillLight
        {
            get
            {
                return this.bFillLightField;
            }
            set
            {
                this.bFillLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bNegativeLight
        {
            get
            {
                return this.bNegativeLightField;
            }
            set
            {
                this.bNegativeLightField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseVDirection
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_BaseVOffset
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_Destroyed
    {

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedColor colorField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedOptions optionsField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedProjector projectorField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedStyle styleField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedTest testField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedVDirection vDirectionField;

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedVOffset vOffsetField;

        private byte radiusField;

        private byte bUseThisLightField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedColor Color
        {
            get
            {
                return this.colorField;
            }
            set
            {
                this.colorField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedOptions Options
        {
            get
            {
                return this.optionsField;
            }
            set
            {
                this.optionsField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedProjector Projector
        {
            get
            {
                return this.projectorField;
            }
            set
            {
                this.projectorField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedStyle Style
        {
            get
            {
                return this.styleField;
            }
            set
            {
                this.styleField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedTest Test
        {
            get
            {
                return this.testField;
            }
            set
            {
                this.testField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedVDirection vDirection
        {
            get
            {
                return this.vDirectionField;
            }
            set
            {
                this.vDirectionField = value;
            }
        }

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedVOffset vOffset
        {
            get
            {
                return this.vOffsetField;
            }
            set
            {
                this.vOffsetField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Radius
        {
            get
            {
                return this.radiusField;
            }
            set
            {
                this.radiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bUseThisLight
        {
            get
            {
                return this.bUseThisLightField;
            }
            set
            {
                this.bUseThisLightField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedColor
    {

        private string clrDiffuseField;

        private byte fDiffuseMultiplierField;

        private byte fHDRDynamicField;

        private byte fSpecularMultiplierField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string clrDiffuse
        {
            get
            {
                return this.clrDiffuseField;
            }
            set
            {
                this.clrDiffuseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fDiffuseMultiplier
        {
            get
            {
                return this.fDiffuseMultiplierField;
            }
            set
            {
                this.fDiffuseMultiplierField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fHDRDynamic
        {
            get
            {
                return this.fHDRDynamicField;
            }
            set
            {
                this.fHDRDynamicField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fSpecularMultiplier
        {
            get
            {
                return this.fSpecularMultiplierField;
            }
            set
            {
                this.fSpecularMultiplierField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedOptions
    {

        private byte bAffectsThisAreaOnlyField;

        private byte nCastShadowsField;

        private byte bDeferredLightField;

        private byte bFakeLightField;

        private byte bIgnoreGeomCasterField;

        private byte bUsedInRealTimeField;

        private byte nViewDistRatioField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bAffectsThisAreaOnly
        {
            get
            {
                return this.bAffectsThisAreaOnlyField;
            }
            set
            {
                this.bAffectsThisAreaOnlyField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nCastShadows
        {
            get
            {
                return this.nCastShadowsField;
            }
            set
            {
                this.nCastShadowsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bDeferredLight
        {
            get
            {
                return this.bDeferredLightField;
            }
            set
            {
                this.bDeferredLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bFakeLight
        {
            get
            {
                return this.bFakeLightField;
            }
            set
            {
                this.bFakeLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bIgnoreGeomCaster
        {
            get
            {
                return this.bIgnoreGeomCasterField;
            }
            set
            {
                this.bIgnoreGeomCasterField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bUsedInRealTime
        {
            get
            {
                return this.bUsedInRealTimeField;
            }
            set
            {
                this.bUsedInRealTimeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nViewDistRatio
        {
            get
            {
                return this.nViewDistRatioField;
            }
            set
            {
                this.nViewDistRatioField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedProjector
    {

        private byte bProjectInAllDirsField;

        private byte fProjectorFovField;

        private byte fProjectorNearPlaneField;

        private string texture_TextureField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bProjectInAllDirs
        {
            get
            {
                return this.bProjectInAllDirsField;
            }
            set
            {
                this.bProjectInAllDirsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fProjectorFov
        {
            get
            {
                return this.fProjectorFovField;
            }
            set
            {
                this.fProjectorFovField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fProjectorNearPlane
        {
            get
            {
                return this.fProjectorNearPlaneField;
            }
            set
            {
                this.fProjectorNearPlaneField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string texture_Texture
        {
            get
            {
                return this.texture_TextureField;
            }
            set
            {
                this.texture_TextureField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedStyle
    {

        private PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedStyleRotation rotationField;

        private byte nAnimationPhaseField;

        private byte fAnimationSpeedField;

        private byte fCoronaDistIntensityFactorField;

        private byte fCoronaDistSizeFactorField;

        private byte fCoronaScaleField;

        private byte nLightStyleField;

        /// <remarks/>
        public PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedStyleRotation Rotation
        {
            get
            {
                return this.rotationField;
            }
            set
            {
                this.rotationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nAnimationPhase
        {
            get
            {
                return this.nAnimationPhaseField;
            }
            set
            {
                this.nAnimationPhaseField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fAnimationSpeed
        {
            get
            {
                return this.fAnimationSpeedField;
            }
            set
            {
                this.fAnimationSpeedField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaDistIntensityFactor
        {
            get
            {
                return this.fCoronaDistIntensityFactorField;
            }
            set
            {
                this.fCoronaDistIntensityFactorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaDistSizeFactor
        {
            get
            {
                return this.fCoronaDistSizeFactorField;
            }
            set
            {
                this.fCoronaDistSizeFactorField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte fCoronaScale
        {
            get
            {
                return this.fCoronaScaleField;
            }
            set
            {
                this.fCoronaScaleField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte nLightStyle
        {
            get
            {
                return this.nLightStyleField;
            }
            set
            {
                this.nLightStyleField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedStyleRotation
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedTest
    {

        private byte bFillLightField;

        private byte bNegativeLightField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bFillLight
        {
            get
            {
                return this.bFillLightField;
            }
            set
            {
                this.bFillLightField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte bNegativeLight
        {
            get
            {
                return this.bNegativeLightField;
            }
            set
            {
                this.bNegativeLightField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedVDirection
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectProperties2LightProperties_DestroyedVOffset
    {

        private byte xField;

        private byte yField;

        private byte zField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte x
        {
            get
            {
                return this.xField;
            }
            set
            {
                this.xField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte y
        {
            get
            {
                return this.yField;
            }
            set
            {
                this.yField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte z
        {
            get
            {
                return this.zField;
            }
            set
            {
                this.zField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectLink
    {

        private string targetIdField;

        private string nameField;

        private string relRotField;

        private string relPosField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string TargetId
        {
            get
            {
                return this.targetIdField;
            }
            set
            {
                this.targetIdField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string RelRot
        {
            get
            {
                return this.relRotField;
            }
            set
            {
                this.relRotField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string RelPos
        {
            get
            {
                return this.relPosField;
            }
            set
            {
                this.relPosField = value;
            }
        }
    }

    /// <remarks/>
    [System.SerializableAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class PrefabsLibraryPrefabObjectObject
    {

        private string typeField;

        private string layerField;

        private string layerGUIDField;

        private string idField;

        private string nameField;

        private string parentField;

        private string posField;

        private sbyte floorNumberField;

        private string rotateField;

        private uint colorRGBField;

        private byte matLayersMaskField;

        private string prefabField;

        private byte noCollisionField;

        private byte outdoorOnlyField;

        private byte castShadowMapsField;

        private byte rainOccluderField;

        private byte supportSecondVisareaField;

        private byte hideableField;

        private byte lodRatioField;

        private byte viewDistRatioField;

        private byte meshIntegrationTypeField;

        private byte notTriangulateField;

        private sbyte aIRadiusField;

        private byte noStaticDecalsField;

        private byte noAmnbShadowCasterField;

        private byte recvWindField;

        private uint rndFlagsField;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Type
        {
            get
            {
                return this.typeField;
            }
            set
            {
                this.typeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Layer
        {
            get
            {
                return this.layerField;
            }
            set
            {
                this.layerField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string LayerGUID
        {
            get
            {
                return this.layerGUIDField;
            }
            set
            {
                this.layerGUIDField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Id
        {
            get
            {
                return this.idField;
            }
            set
            {
                this.idField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Name
        {
            get
            {
                return this.nameField;
            }
            set
            {
                this.nameField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Parent
        {
            get
            {
                return this.parentField;
            }
            set
            {
                this.parentField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Pos
        {
            get
            {
                return this.posField;
            }
            set
            {
                this.posField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte FloorNumber
        {
            get
            {
                return this.floorNumberField;
            }
            set
            {
                this.floorNumberField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Rotate
        {
            get
            {
                return this.rotateField;
            }
            set
            {
                this.rotateField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint ColorRGB
        {
            get
            {
                return this.colorRGBField;
            }
            set
            {
                this.colorRGBField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte MatLayersMask
        {
            get
            {
                return this.matLayersMaskField;
            }
            set
            {
                this.matLayersMaskField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string Prefab
        {
            get
            {
                return this.prefabField;
            }
            set
            {
                this.prefabField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NoCollision
        {
            get
            {
                return this.noCollisionField;
            }
            set
            {
                this.noCollisionField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte OutdoorOnly
        {
            get
            {
                return this.outdoorOnlyField;
            }
            set
            {
                this.outdoorOnlyField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte CastShadowMaps
        {
            get
            {
                return this.castShadowMapsField;
            }
            set
            {
                this.castShadowMapsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte RainOccluder
        {
            get
            {
                return this.rainOccluderField;
            }
            set
            {
                this.rainOccluderField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte SupportSecondVisarea
        {
            get
            {
                return this.supportSecondVisareaField;
            }
            set
            {
                this.supportSecondVisareaField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte Hideable
        {
            get
            {
                return this.hideableField;
            }
            set
            {
                this.hideableField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte LodRatio
        {
            get
            {
                return this.lodRatioField;
            }
            set
            {
                this.lodRatioField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte ViewDistRatio
        {
            get
            {
                return this.viewDistRatioField;
            }
            set
            {
                this.viewDistRatioField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte MeshIntegrationType
        {
            get
            {
                return this.meshIntegrationTypeField;
            }
            set
            {
                this.meshIntegrationTypeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NotTriangulate
        {
            get
            {
                return this.notTriangulateField;
            }
            set
            {
                this.notTriangulateField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public sbyte AIRadius
        {
            get
            {
                return this.aIRadiusField;
            }
            set
            {
                this.aIRadiusField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NoStaticDecals
        {
            get
            {
                return this.noStaticDecalsField;
            }
            set
            {
                this.noStaticDecalsField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte NoAmnbShadowCaster
        {
            get
            {
                return this.noAmnbShadowCasterField;
            }
            set
            {
                this.noAmnbShadowCasterField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public byte RecvWind
        {
            get
            {
                return this.recvWindField;
            }
            set
            {
                this.recvWindField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public uint RndFlags
        {
            get
            {
                return this.rndFlagsField;
            }
            set
            {
                this.rndFlagsField = value;
            }
        }


    }
}