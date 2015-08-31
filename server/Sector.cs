using Json;
using Maps.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Reflection;

namespace Maps
{

#if NOT_YET_IMPLEMENTED
    [XmlRoot( ElementName = "Setting" )]
    public class Setting
    {
        public Setting()
        {
            Rifts = new List<string>();
            Borders = new List<string>();
            Worlds = new List<string>();
            SectorCollections = new List<string>();
        }

        private SectorMap m_map;
        public SectorMap Map { get { return m_map; } }

        public List<string> Rifts { get; set; }
        public List<string> Borders { get; set; }
        public List<string> Worlds { get; set; }
        public List<string> SectorCollections { get; set; }

        public static Setting GetSetting( string name, ResourceManager resourceManager )
        {
            // TODO: Cache these statically
            Setting setting = resourceManager.GetXmlFileObject( String.Format( @"~/res/Setting_{0}.xml", name ), typeof( Setting ), false ) as Setting;

            setting.m_map = null; // new SectorMap( /*setting.m_sectorCollections*/ );

            return setting;
        }
    }
#endif
    [Serializable]
    public class MapNotInitializedException : Exception
    {
        public MapNotInitializedException()
            :
            base("SectorMap data not initialized")
        {
        }
    }

    public struct SectorMetafileEntry
    {
        public string filename;
        public List<string> tags;
        public SectorMetafileEntry(string filename, List<string> tags)
        {
            this.tags = tags;
            this.filename = filename;
        }
    }

    public class SectorMap
    {
        public const string DefaultSetting = "OTU";

        private static Object s_lock = new Object();

        private static SectorMap s_OTU;

        private SectorCollection m_sectors;
        public IList<Sector> Sectors { get { return m_sectors.Sectors; } }

        private Dictionary<string, Sector> m_nameMap = new Dictionary<string, Sector>(StringComparer.InvariantCultureIgnoreCase);
        private Dictionary<Point, Sector> m_locationMap = new Dictionary<Point, Sector>();

        private SectorMap(List<SectorMetafileEntry> metafiles, ResourceManager resourceManager)
        {
            foreach (var metafile in metafiles)
            {
                SectorCollection collection = resourceManager.GetXmlFileObject(metafile.filename, typeof(SectorCollection), cache: false) as SectorCollection;
                foreach (var sector in collection.Sectors)
                    sector.Tags.AddRange(metafile.tags);

                if (m_sectors == null)
                    m_sectors = collection;
                else
                    m_sectors.Merge(collection);
            }

            m_nameMap.Clear();
            m_locationMap.Clear();

            foreach (var sector in m_sectors.Sectors)
            {
                if (sector.MetadataFile != null)
                {
                    Sector metadata = resourceManager.GetXmlFileObject(@"~/res/Sectors/" + sector.MetadataFile, typeof(Sector), cache: false) as Sector;
                    sector.Merge(metadata);
                }

                m_locationMap.Add(sector.Location, sector);

                foreach (var name in sector.Names)
                {
                    if (!m_nameMap.ContainsKey(name.Text))
                        m_nameMap.Add(name.Text, sector);

                    // Automatically alias "SpinwardMarches"
                    string spaceless = name.Text.Replace(" ", "");
                    if (spaceless != name.Text && !m_nameMap.ContainsKey(spaceless))
                        m_nameMap.Add(spaceless, sector);
                }

                if (!String.IsNullOrEmpty(sector.Abbreviation) && !m_nameMap.ContainsKey(sector.Abbreviation))
                    m_nameMap.Add(sector.Abbreviation, sector);
            }
        }

        public static SectorMap FromName(string settingName, ResourceManager resourceManager)
        {
            if (settingName != SectorMap.DefaultSetting)
                throw new ArgumentException("Only OTU setting is currently supported.");

            lock (SectorMap.s_lock)
            {
                if (s_OTU == null)
                {
                    List<SectorMetafileEntry> files = new List<SectorMetafileEntry>
                    {
                        new SectorMetafileEntry(@"~/res/legend.xml", new List<string> { "meta" } ),
                        new SectorMetafileEntry(@"~/res/sectors.xml", new List<string> { "OTU" } ),
                        new SectorMetafileEntry(@"~/res/faraway.xml", new List<string> { "Faraway" } ),
                        new SectorMetafileEntry(@"~/res/ZhodaniCoreRoute.xml", new List<string> { "ZCR" } )
                    };

                    s_OTU = new SectorMap(files, resourceManager);
                }
            }

            return s_OTU;
        }

        public static void Flush()
        {
            lock (SectorMap.s_lock)
            {
                s_OTU = null;
            }
        }

        public static Sector FromName(string settingName, string sectorName)
        {
            // TODO: Having this method supports deserializing data that refers generically to
            // sectors by name.
            //  * Consider decoupling sector *names* from sector data
            //  * Consider Location (the offender) having a default setting

            if (settingName == null)
                settingName = SectorMap.DefaultSetting;

            if (settingName != SectorMap.DefaultSetting)
                throw new ArgumentException("Only OTU setting is currently supported");

            if (s_OTU == null)
                throw new MapNotInitializedException();

            return s_OTU.FromName(sectorName);
        }

        public Sector FromName(string sectorName)
        {
            if (m_sectors == null || m_nameMap == null)
                throw new MapNotInitializedException();

            Sector sector;
            m_nameMap.TryGetValue(sectorName, out sector); // Using indexer throws exception, this is more performant
            return sector;
        }

        public Sector FromLocation(int x, int y) { return FromLocation(new Point(x, y)); }
        public Sector FromLocation(Point pt)
        {
            if (m_sectors == null || m_locationMap == null)
                throw new MapNotInitializedException();

            Sector sector;
            m_locationMap.TryGetValue(pt, out sector);
            return sector;
        }
    }

    public class Sector : MetadataItem
    {
        public Sector()
        {
            this.Names = new List<Name>();

        }

        public Sector(Stream stream, string mediaType, ErrorLogger errors)
            : this()
        {
            WorldCollection wc = new WorldCollection();
            wc.Deserialize(stream, mediaType, errors);
            foreach (World world in wc)
                world.Sector = this;
            m_data = wc;
        }

        public int X { get { return Location.X; } set { Location = new Point(value, Location.Y); } }
        public int Y { get { return Location.Y; } set { Location = new Point(Location.X, value); } }
        public Point Location { get; set; }

        [XmlAttribute]
        public string Abbreviation { get; set; }

        [XmlAttribute]
        public string Label { get; set; }

        [XmlElement("Name")]
        public List<Name> Names { get; set; }

        public string Domain { get; set; }

        public string AlphaQuadrant { get; set; }
        public string BetaQuadrant { get; set; }
        public string GammaQuadrant { get; set; }
        public string DeltaQuadrant { get; set; }

        private MetadataCollection<Subsector> m_subsectors = new MetadataCollection<Subsector>();
        private MetadataCollection<Route> m_routes = new MetadataCollection<Route>();
        private MetadataCollection<Label> m_labels = new MetadataCollection<Label>();
        private MetadataCollection<Border> m_borders = new MetadataCollection<Border>();
        private MetadataCollection<Allegiance> m_allegiances = new MetadataCollection<Allegiance>();
        private MetadataCollection<Product> m_products = new MetadataCollection<Product>();

        [XmlAttribute]
        [DefaultValue(false)]
        public bool Selected { get; set; }

        [XmlElement("Product")]
        public MetadataCollection<Product> Products { get { return m_products; } }

        public MetadataCollection<Subsector> Subsectors { get { return m_subsectors; } }
        [XmlIgnore]
        public bool SubsectorsSpecified { get { return m_subsectors.Count() > 0; } }

        public MetadataCollection<Border> Borders { get { return m_borders; } }
        [XmlIgnore]
        public bool BordersSpecified { get { return m_borders.Count() > 0; } }

        public MetadataCollection<Label> Labels { get { return m_labels; } }
        [XmlIgnore]
        public bool LabelsSpecified { get { return m_labels.Count() > 0; } }

        public MetadataCollection<Route> Routes { get { return m_routes; } }
        [XmlIgnore]
        public bool RoutesSpecified { get { return m_routes.Count() > 0; } }

        public MetadataCollection<Allegiance> Allegiances { get { return m_allegiances; } }
        [XmlIgnore]
        public bool AllegiancesSpecified { get { return m_allegiances.Count() > 0; } }

        public string Credits { get; set; }

        public void Merge(Sector metadataSource)
        {
            if (metadataSource == null)
                throw new ArgumentNullException("metadataSource");

            // TODO: This is very fragile; if a new type is added to Sector we need to add more code here.

            if (metadataSource.Names.Any()) this.Names = metadataSource.Names;
            if (metadataSource.DataFile != null) this.DataFile = metadataSource.DataFile;

            this.Subsectors.AddRange(metadataSource.Subsectors);
            this.Allegiances.AddRange(metadataSource.Allegiances);
            this.Borders.AddRange(metadataSource.Borders);
            this.Routes.AddRange(metadataSource.Routes);
            this.Labels.AddRange(metadataSource.Labels);
            this.Credits = metadataSource.Credits;
            this.Products.AddRange(metadataSource.Products);
            this.StylesheetText = metadataSource.StylesheetText;
        }

        [XmlAttribute("Tags"), JsonName("Tags")]
        public string TagString
        {
            get { return String.Join(" ", m_tags); }
            set
            {
                m_tags.Clear();
                if (String.IsNullOrWhiteSpace(value))
                    return;
                m_tags.AddRange(value.Split());
            }
        }

        [XmlIgnore, JsonIgnore]
        public OrderedHashSet<string> Tags { get { return m_tags; } }
        private OrderedHashSet<string> m_tags = new OrderedHashSet<string>();

        public Allegiance GetAllegianceFromCode(string code)
        {
            // TODO: Consider hashtable
            Allegiance alleg = Allegiances.Where(a => a.T5Code == code).FirstOrDefault();
            return alleg ?? SecondSurvey.GetStockAllegianceFromCode(code);
        }

        /// <summary>
        /// Map allegiances like "Sy" for "Sylean Federation" worlds to "Im"
        /// </summary>
        /// <param name="code">The allegiance code to map, e.g. "Sy"</param>
        /// <returns>The base allegiance code, e.g. "Im", or the original code if none.</returns>
        public string AllegianceCodeToBaseAllegianceCode(string code)
        {
            var alleg = GetAllegianceFromCode(code);
            if (alleg != null && !String.IsNullOrEmpty(alleg.Base))
                return alleg.Base;
            return code;
        }

        public DataFile DataFile { get; set; }

        public string MetadataFile { get; set; }

        private WorldCollection m_data;

        public Subsector this[char alpha]
        {
            get
            {
                return Subsectors.Where(ss => ss.Index != null && ss.Index[0] == alpha).FirstOrDefault();
            }
        }

        public Subsector this[int index]
        {
            get
            {
                if (index < 0 || index > 15)
                    throw new ArgumentOutOfRangeException("index");

                char alpha = (char)('A' + index);

                return this[alpha];
            }
        }

        public Subsector this[int x, int y]
        {
            get
            {
                if (x < 0 || x > 3)
                    throw new ArgumentOutOfRangeException("x");
                if (y < 0 || y > 3)
                    throw new ArgumentOutOfRangeException("y");

                return this[x + (y * 4)];
            }
        }

        public int SubsectorIndexFor(string label)
        {
            if (String.IsNullOrWhiteSpace(label))
                return -1;
            Subsector subsector;
            if (label.Length == 1 && 'A' <= label.ToUpperInvariant()[0] && label.ToUpperInvariant()[0] <= 'P')
                return (int)label.ToUpperInvariant()[0] - (int)'A';

            subsector = Subsectors.Where(ss => !String.IsNullOrEmpty(ss.Name) && ss.Name.Equals(label, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            if (subsector == null)
                return -1;
            return subsector.IndexNumber;
        }

        public static int QuadrantIndexFor(string label)
        {
            switch (label.ToLowerInvariant())
            {
                case "alpha": return 0;
                case "beta": return 1;
                case "gamma": return 2;
                case "delta": return 3;
            }
            return -1;
        }

        public WorldCollection GetWorlds(ResourceManager resourceManager, bool cacheResults = true)
        {
            lock (this)
            {
                // Have it cached - just return it
                if (m_data != null)
                    return m_data;

                // Can't look it up; failure case
                if (DataFile == null)
                    return null;

                // Otherwise, look it up
                WorldCollection data = resourceManager.GetDeserializableFileObject(@"~/res/Sectors/" + DataFile, typeof(WorldCollection), cacheResults: false, mediaType: DataFile.Type) as WorldCollection;
                foreach (World world in data)
                    world.Sector = this;

                if (cacheResults)
                    m_data = data;

                return data;
            }
        }

        public void Serialize(ResourceManager resourceManager, TextWriter writer, string mediaType, bool includeMetadata = true, bool includeHeader = true, bool sscoords = false, WorldFilter filter = null)
        {
            WorldCollection worlds = GetWorlds(resourceManager);

            // TODO: less hacky T5 support
            bool isT5 = (mediaType == "TabDelimited" || mediaType == "SecondSurvey");

            if (mediaType == "TabDelimited")
            {
                if (worlds != null)
                    worlds.Serialize(writer, mediaType, includeHeader: includeHeader, filter: filter);
                return;
            }

            if (includeMetadata)
            {
                // Header
                //
                writer.WriteLine("# Generated by http://www.travellermap.com");
                writer.WriteLine("# " + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", DateTimeFormatInfo.InvariantInfo));
                writer.WriteLine();

                writer.WriteLine("# {0}", this.Names[0]);
                writer.WriteLine("# {0},{1}", this.X, this.Y);

                writer.WriteLine();
                foreach (var name in Names)
                {
                    if (name.Lang != null)
                        writer.WriteLine("# Name: {0} ({1})", name.Text, name.Lang);
                    else
                        writer.WriteLine("# Name: {0}", name);
                }

                if (Credits != null)
                {
                    string stripped = Regex.Replace(Credits, "<.*?>", "");
                    stripped = Regex.Replace(stripped, @"\s+", " ");
                    stripped = stripped.Trim();
                    writer.WriteLine();
                    writer.WriteLine("# Credits: {0}", stripped);
                }

                if (DataFile != null)
                {
                    writer.WriteLine();
                    if (DataFile.Era != null) { writer.WriteLine("# Era: {0}", DataFile.Era); }
                    writer.WriteLine();

                    if (DataFile.Author != null) { writer.WriteLine("# Author:    {0}", DataFile.Author); }
                    if (DataFile.Publisher != null) { writer.WriteLine("# Publisher: {0}", DataFile.Publisher); }
                    if (DataFile.Copyright != null) { writer.WriteLine("# Copyright: {0}", DataFile.Copyright); }
                    if (DataFile.Source != null) { writer.WriteLine("# Source:    {0}", DataFile.Source); }
                    if (DataFile.Ref != null) { writer.WriteLine("# Ref:       {0}", DataFile.Ref); }
                }

                writer.WriteLine();
                for (int i = 0; i < 16; ++i)
                {
                    char c = (char)('A' + i);
                    Subsector ss = this[c];
                    writer.WriteLine("# Subsector {0}: {1}", c, (ss != null ? ss.Name : ""));
                }
                writer.WriteLine();
            }

            if (worlds == null)
            {
                if (includeMetadata)
                    writer.WriteLine("# No world data available");
                return;
            }

            // Allegiances
            if (includeMetadata)
            {
                // Use codes as present in the data, to match the worlds
                foreach (string code in worlds.AllegianceCodes().OrderBy(s => s))
                {
                    var alleg = GetAllegianceFromCode(code);
                    if (alleg != null)
                        writer.WriteLine("# Alleg: {0}: \"{1}\"", isT5 ? code : SecondSurvey.T5AllegianceCodeToLegacyCode(code), alleg.Name);
                }
                writer.WriteLine();
            }

            // Worlds
            worlds.Serialize(writer, mediaType, includeHeader: includeHeader, sscoords: sscoords, filter: filter);
        }

        // TODO: Move this elsewhere
        public class ClipPath
        {
            public readonly PointF[] clipPathPoints;
            public readonly byte[] clipPathPointTypes;
            public readonly RectangleF bounds;

            public ClipPath(Sector sector, PathUtil.PathType borderPathType)
            {
                float[] edgex, edgey;
                RenderUtil.HexEdges(borderPathType, out edgex, out edgey);

                Rectangle bounds = sector.Bounds;
                List<Point> clip = new List<Point>();

                clip.AddRange(Util.Sequence(1, Astrometrics.SectorWidth).Select(x => new Point(x, 1)));
                clip.AddRange(Util.Sequence(2, Astrometrics.SectorHeight).Select(y => new Point(Astrometrics.SectorWidth, y)));
                clip.AddRange(Util.Sequence(Astrometrics.SectorWidth - 1, 1).Select(x => new Point(x, Astrometrics.SectorHeight)));
                clip.AddRange(Util.Sequence(Astrometrics.SectorHeight - 1, 1).Select(y => new Point(1, y)));

                for (int i = 0; i < clip.Count; ++i)
                {
                    Point pt = clip[i];
                    pt.Offset(bounds.Location);
                    clip[i] = pt;
                }

                PathUtil.ComputeBorderPath(clip, edgex, edgey, out clipPathPoints, out clipPathPointTypes);

                PointF min = clipPathPoints[0];
                PointF max = clipPathPoints[0];
                for (int i = 1; i < clipPathPoints.Length; ++i)
                {
                    PointF pt = clipPathPoints[i];
                    if (pt.X < min.X)
                        min.X = pt.X;
                    if (pt.Y < min.Y)
                        min.Y = pt.Y;
                    if (pt.X > max.X)
                        max.X = pt.X;
                    if (pt.Y > max.Y)
                        max.Y = pt.Y;
                }
                this.bounds = new RectangleF(min, new SizeF(max.X - min.X, max.Y - min.Y));
            }
        }

        private ClipPath[] clipPathsCache = new ClipPath[(int)PathUtil.PathType.TypeCount];
        public ClipPath ComputeClipPath(PathUtil.PathType type)
        {
            lock (this)
            {
                if (clipPathsCache[(int)type] == null)
                    clipPathsCache[(int)type] = new ClipPath(this, type);
            }

            return clipPathsCache[(int)type];
        }

        [XmlIgnore, JsonIgnore]
        public Rectangle Bounds
        {
            get
            {
                return new Rectangle(
                        (this.Location.X * Astrometrics.SectorWidth) - Astrometrics.ReferenceHex.X,
                        (this.Location.Y * Astrometrics.SectorHeight) - Astrometrics.ReferenceHex.Y,
                        Astrometrics.SectorWidth, Astrometrics.SectorHeight
                    );
            }
        }

        public Rectangle SubsectorBounds(int index)
        {
            return new Rectangle(
                (this.Location.X * Astrometrics.SectorWidth) - Astrometrics.ReferenceHex.X + (Astrometrics.SubsectorWidth * (index % 4)),
                (this.Location.Y * Astrometrics.SectorHeight) - Astrometrics.ReferenceHex.Y + (Astrometrics.SubsectorHeight * (index / 4)),
                Astrometrics.SubsectorWidth,
                Astrometrics.SubsectorHeight);
        }

        public Rectangle QuadrantBounds(int index)
        {
            return new Rectangle(
                (this.Location.X * Astrometrics.SectorWidth) - Astrometrics.ReferenceHex.X + (Astrometrics.SubsectorWidth * 2 * (index % 2)),
                (this.Location.Y * Astrometrics.SectorHeight) - Astrometrics.ReferenceHex.Y + (Astrometrics.SubsectorHeight * 2 * (index / 2)),
                Astrometrics.SubsectorWidth * 2,
                Astrometrics.SubsectorHeight * 2);
        }

        [XmlIgnore, JsonIgnore]
        public Point Center
        {
            get
            {
                return Astrometrics.LocationToCoordinates(Location, new Point(Astrometrics.SectorWidth / 2, Astrometrics.SectorHeight / 2));
            }
        }

        public Point SubsectorCenter(int index)
        {
            int ssx = index % 4;
            int ssy = index / 4;
            return Astrometrics.LocationToCoordinates(this.Location,
                new Point(Astrometrics.SubsectorWidth * (2 * ssx + 1) / 2, Astrometrics.SubsectorHeight * (2 * ssy + 1) / 2));
        }

        private static SectorStylesheet s_defaultStyleSheet;
        static Sector()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream(@"Maps.res.styles.otu.css");
            s_defaultStyleSheet = SectorStylesheet.Parse(new StreamReader(stream));
        }

        [XmlIgnore, JsonIgnore]
        public SectorStylesheet Stylesheet { get; set; }

        public SectorStylesheet.StyleResult ApplyStylesheet(string element, string code)
        {
            return (Stylesheet ?? s_defaultStyleSheet).Apply(element, code);
        }

        [XmlElement("Stylesheet"), JsonName("Stylesheet")]
        public string StylesheetText
        {
            get { return stylesheetText; }
            set
            {
                stylesheetText = value;
                if (value != null)
                {
                    Stylesheet = SectorStylesheet.Parse(stylesheetText);
                    Stylesheet.Parent = s_defaultStyleSheet;
                }
            }
        }
        private string stylesheetText;

    }

    public class Product : MetadataItem
    {
    }

    public class Name
    {
        public Name() { }
        public Name(string text = "", string lang = null)
        {
            Text = text;
            Lang = lang;
        }

        [XmlText]
        public string Text { get; set; }

        [XmlAttribute]
        [DefaultValueAttribute("")]
        public string Lang { get; set; }

        [XmlAttribute]
        public string Source { get; set; }

        public override string ToString()
        {
            return Text;
        }
    }

    public class DataFile : MetadataItem
    {
        public DataFile()
        {
            FileName = String.Empty;
            Type = "SEC";
        }

        public override string ToString()
        {
            return FileName;
        }

        [XmlText]
        public string FileName { get; set; }

        [XmlAttribute]
        [DefaultValueAttribute("")]
        public string Type { get; set; }
    }

    public class Subsector : MetadataItem
    {
        public Subsector()
        {
            Name = String.Empty;
            Index = String.Empty;
        }

        [XmlText]
        public string Name { get; set; }

        [XmlAttribute]
        public string Index { get; set; }

        public int IndexNumber
        {
            get
            {
                if (String.IsNullOrWhiteSpace(Index))
                    return -1;
                return (int)Index[0] - (int)'A';
            }
        }
    }

    sealed public class Allegiance : IAllegiance
    {
        public Allegiance() { }
        public Allegiance(string code, string name)
        {
            this.Name = name;
            this.T5Code = code;
            this.LegacyCode = code;
        }
        public Allegiance(string t5code, string legacyCode, string name)
        {
            this.Name = name;
            this.T5Code = t5code;
            this.LegacyCode = legacyCode;
        }
        public Allegiance(string t5code, string legacyCode, string baseCode, string name)
        {
            this.Name = name;
            this.T5Code = t5code;
            this.LegacyCode = legacyCode;
            this.Base = baseCode;
        }

        [XmlText]
        public string Name { get; set; }

        /// <summary>
        /// The four letter (or, in legacy data, two) code for the allegiance, e.g. "As" for Aslan, "Va" for Vargr, 
        /// "Im" for Imperium, and so on.
        /// </summary>
        [XmlAttribute("Code"), JsonName("Code")]
        public string T5Code { get; set; }

        [XmlIgnore, JsonIgnore]
        public string LegacyCode { get { return String.IsNullOrEmpty(m_legacyCode) ? T5Code : m_legacyCode; } set { m_legacyCode = value; } }
        private string m_legacyCode;

        /// <summary>
        /// The code for the fundamental allegiance type. For example, the various MT-era Rebellion 
        /// factions (e.g. Domain of Deneb, "Dd") and cultural regions (Sylean Federation Worlds, "Sy") 
        /// have the base code "Im" for Imperium. 
        /// 
        /// Base codes should be unique across Charted Space, but other allegiance codes may not be.
        /// 
        /// This is not the same as e.g. naval/scout bases, but it can be used to more easily distinguish
        //  e.g. Imperial naval bases from Vargr naval bases (e.g. "Im"+"N" vs. "Va"+"N")
        /// </summary>
        [XmlAttribute]
        public string Base { get; set; }

        private class AllegianceCollection : List<Allegiance>
        {
            public void Add(string code, string name) { Add(new Allegiance(code, name)); }
        }

        string IAllegiance.Allegiance { get { return this.T5Code; } }
    }

    public interface IAllegiance
    {
        string Allegiance { get; }
    }

    public class Border : IAllegiance
    {
        public Border()
        {
            this.ShowLabel = true;
            this.Path = new int[0];
        }

        public Border(string path, string color = null)
            : this()
        {
            this.PathString = path;
            if (color != null)
                this.ColorHtml = color;
        }

        [XmlAttribute]
        [DefaultValue(true)]
        public bool ShowLabel { get; set; }

        [XmlAttribute]
        public bool WrapLabel { get; set; }

        [XmlIgnore, JsonIgnore]
        public Color? Color { get; set; }
        [XmlAttribute("Color"), JsonName("Color")]
        public string ColorHtml { get { return Color.HasValue ? ColorTranslator.ToHtml(Color.Value) : null; } set { Color = ColorTranslator.FromHtml(value); } }

        [XmlAttribute]
        public string Allegiance { get; set; }

        [XmlIgnore, JsonIgnore]
        public int[] Path { get; set; }

        [XmlIgnoreAttribute, JsonIgnore]
        public Point LabelPosition { get; set; }

        [XmlAttribute("LabelPosition"), JsonName("LabelPosition")]
        public string LabelPositionHex
        {
            get { return Astrometrics.PointToHex(LabelPosition); }
            set { LabelPosition = Astrometrics.HexToPoint(value); }
        }

        [XmlAttribute]
        public string Label { get; set; }

        [XmlIgnore]
        public LineStyle? Style { get; set; }
        [XmlAttribute("Style"), JsonIgnore]
        public LineStyle _Style { get { return Style.Value; } set { Style = value; } }
        public bool ShouldSerialize_Style() { return Style.HasValue; }


        [XmlText, JsonName("Path")]
        public string PathString
        {
            get
            {
                string[] hexes = new string[Path.Length];
                for (int i = 0; i < Path.Length; i++)
                    hexes[i] = Astrometrics.IntToHex(Path[i]);
                return String.Join(" ", hexes);
            }
            set
            {
                // Track the "bounding box" (in hex space)
                Point min = new Point(99, 99);
                Point max = new Point(0, 0);

                string[] hexes = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                List<int> list = new List<int>(hexes.Length);

                for (int i = 0; i < hexes.Length; i++)
                {
                    int hex;
                    if (Int32.TryParse(hexes[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out hex))
                    {
                        list.Add(hex);

                        int x = hex / 100;
                        int y = hex % 100;

                        if (x < min.X) min.X = x;
                        if (y < min.Y) min.Y = y;

                        if (x > max.X) max.X = x;
                        if (y > max.Y) max.Y = y;
                    }
                }

                Path = list.ToArray();

                // If no position was set, use the center of the "bounding box"
                if (LabelPosition.IsEmpty)
                    LabelPosition = new Point((min.X + max.X + 1) / 2, (min.Y + max.Y + 1) / 2); // "+ 1" to round up

                Extends = (min.X < 1 || min.Y < 1 || max.X > Astrometrics.SectorWidth || max.Y > Astrometrics.SectorHeight);
            }
        }

        [XmlIgnoreAttribute, JsonIgnore]
        public bool Extends { get; set; }

        private BorderPath[] borderPathsCache = new BorderPath[(int)PathUtil.PathType.TypeCount];
        public BorderPath ComputeGraphicsPath(Sector sector, PathUtil.PathType type)
        {
            lock (this)
            {
                if (borderPathsCache[(int)type] == null)
                    borderPathsCache[(int)type] = new BorderPath(this, sector, type);
            }

            return borderPathsCache[(int)type];
        }

    }

    public enum LineStyle
    {
        Solid = 0, // Default
        Dashed,
        Dotted,
        None
    }

    public class Route : IAllegiance
    {
        public Route()
        {
        }

        public Route(Point? startOffset = null, int start = 0, Point? endOffset = null, int end = 0, string color = null)
            : this()
        {
            StartOffset = startOffset ?? Point.Empty;
            Start = start;
            EndOffset = endOffset ?? Point.Empty;
            End = end;
            if (color != null)
                ColorHtml = color;
        }

        [XmlIgnoreAttribute, JsonIgnore]
        public int Start { get; set; }

        [XmlIgnoreAttribute, JsonIgnore]
        public int End { get; set; }

        [XmlAttribute("Start"), JsonName("Start")]
        public string StartHex
        {
            get { return Astrometrics.IntToHex(Start); }
            set { Start = Astrometrics.HexToInt(value); }
        }

        [XmlAttribute("End"), JsonName("End")]
        public string EndHex
        {
            get { return Astrometrics.IntToHex(End); }
            set { End = Astrometrics.HexToInt(value); }
        }

        [XmlIgnoreAttribute, JsonIgnore]
        public Point StartOffset { get; set; }

        [XmlIgnoreAttribute, JsonIgnore]
        public Point EndOffset { get; set; }

        [XmlIgnoreAttribute, JsonIgnore]
        public Point StartPoint { get { return new Point(Start / 100, Start % 100); } }

        [XmlIgnoreAttribute, JsonIgnore]
        public Point EndPoint { get { return new Point(End / 100, End % 100); } }

        [XmlAttribute("StartOffsetX")]
        [DefaultValueAttribute(0)]
        public int StartOffsetX { get { return StartOffset.X; } set { StartOffset = new Point(value, StartOffset.Y); } }

        [XmlAttribute("StartOffsetY")]
        [DefaultValueAttribute(0)]
        public int StartOffsetY { get { return StartOffset.Y; } set { StartOffset = new Point(StartOffset.X, value); } }

        [XmlAttribute("EndOffsetX")]
        [DefaultValueAttribute(0)]
        public int EndOffsetX { get { return EndOffset.X; } set { EndOffset = new Point(value, EndOffset.Y); } }

        [XmlAttribute("EndOffsetY")]
        [DefaultValueAttribute(0)]
        public int EndOffsetY { get { return EndOffset.Y; } set { EndOffset = new Point(EndOffset.X, value); } }


        [XmlIgnore]
        public LineStyle? Style { get; set; }
        [XmlAttribute("Style"), JsonIgnore]
        public LineStyle _Style { get { return Style.Value; } set { Style = value; } }
        public bool ShouldSerialize_Style() { return Style.HasValue; }

        [XmlIgnore]
        public float? Width { get; set; }
        [XmlAttribute("Width"), JsonIgnore]
        public float _Width { get { return Width.Value; } set { Width = value; } }
        public bool ShouldSerialize_Width() { return Width.HasValue; }

        [XmlIgnore, JsonIgnore]
        public Color? Color { get; set; }
        [XmlAttribute("Color"), JsonName("Color")]
        public string ColorHtml { get { return Color.HasValue ? ColorTranslator.ToHtml(Color.Value) : null; } set { Color = ColorTranslator.FromHtml(value); } }

        [XmlAttribute]
        public string Allegiance { get; set; }

        [XmlAttribute]
        public string Type { get; set; }

        public override string ToString()
        {
            var s = "";

            if (StartOffsetX != 0 || StartOffsetY != 0)
            {
                s += StartOffsetX.ToString(CultureInfo.InvariantCulture);
                s += " ";
                s += StartOffsetY.ToString(CultureInfo.InvariantCulture);
                s += " ";
            }
            s += StartHex;
            s += " ";
            if (EndOffsetX != 0 || EndOffsetY != 0)
            {
                s += EndOffsetX.ToString(CultureInfo.InvariantCulture);
                s += " ";
                s += EndOffsetY.ToString(CultureInfo.InvariantCulture);
                s += " ";
            }
            s += EndHex;
            return s;
        }
    }

    public class Label : IAllegiance
    {
        public Label()
        {
            Color = DefaultColor;
        }
        public Label(int hex, string text)
            : this()
        {
            Hex = hex;
            Text = text;
        }

        public static Color DefaultColor { get { return Color.Yellow; } }

        [XmlAttribute]
        public int Hex { get; set; }

        [XmlAttribute]
        public string Allegiance { get; set; }

        [XmlIgnoreAttribute, JsonIgnore]
        public Color Color { get; set; }

        [XmlAttribute("Color"), JsonName("Color")]
        public string ColorHtml { get { return ColorTranslator.ToHtml(Color); } set { Color = ColorTranslator.FromHtml(value); } }

        [XmlAttribute]
        public string Size { get; set; }

        [XmlAttribute]
        public float OffsetY { get; set; }

        [XmlAttribute]
        // TODO: Unused
        public string RenderType { get; set; }

        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "Sectors")]
    public class SectorCollection
    {
        [XmlElement("Sector")]
        public List<Sector> Sectors { get; set; }

        public void Merge(SectorCollection otherCollection)
        {
            if (otherCollection == null)
                throw new ArgumentNullException("otherCollection");

            if (Sectors == null)
                Sectors = otherCollection.Sectors;
            else if (otherCollection.Sectors != null)
                Sectors.AddRange(otherCollection.Sectors);
        }
    }

}
