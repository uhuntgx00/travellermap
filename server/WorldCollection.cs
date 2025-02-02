using Maps.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;

namespace Maps
{
    /// <summary>
    /// Summary description for SectorData.
    /// </summary>
    public class WorldCollection : IDeserializable, IEnumerable<World>
    {
        public WorldCollection()
        {
#if DEBUG
            m_errors = new ErrorLogger();
#endif
        }
        
        private World[,] m_worlds = new World[Astrometrics.SectorWidth, Astrometrics.SectorHeight];
        public World this[int x, int y]
        {
            get
            {
                if (x < 1 || x > Astrometrics.SectorWidth)
                    throw new ArgumentOutOfRangeException("x");
                if (y < 1 || y > Astrometrics.SectorHeight)
                    throw new ArgumentOutOfRangeException("y");

                return m_worlds[x - 1, y - 1];
            }
            set
            {
                if (x < 1 || x > Astrometrics.SectorWidth)
                    throw new ArgumentOutOfRangeException("x");
                if (y < 1 || y > Astrometrics.SectorHeight)
                    throw new ArgumentOutOfRangeException("y");

                m_worlds[x - 1, y - 1] = value;
            }
        }

        public IEnumerator<World> GetEnumerator()
        {
            for (int x = 1; x <= Astrometrics.SectorWidth; ++x)
            {
                for (int y = 1; y <= Astrometrics.SectorHeight; ++y)
                {
                    World world = m_worlds[x - 1, y - 1];
                    if (world != null)
                        yield return world;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        private ErrorLogger m_errors = null;
        public ErrorLogger ErrorList { get { return m_errors; } }

        public void Serialize(TextWriter writer, string mediaType, bool includeHeader = true, bool sscoords = false, WorldFilter filter = null)
        {
            SectorFileSerializer.ForType(mediaType).Serialize(writer, this.Where(world => filter == null || filter(world)), includeHeader: includeHeader, sscoords: sscoords);
        }

        public void Deserialize(Stream stream, string mediaType, ErrorLogger errors = null)
        {
            if (mediaType == null || mediaType == MediaTypeNames.Text.Plain || mediaType == MediaTypeNames.Application.Octet)
                mediaType = SectorFileParser.SniffType(stream);
            SectorFileParser parser = SectorFileParser.ForType(mediaType);
            parser.Parse(stream, this, errors);
            if (errors != null && !errors.Empty)
            {
                errors.Prepend(ErrorLogger.Severity.Warning, String.Format("Parsing as: {0}", parser.Name));
            }
        }

        public HashSet<string> AllegianceCodes()
        {
            var set = new HashSet<string>();
            foreach (var world in this)
                set.Add(world.Allegiance);
            return set;
        }
    }
}
