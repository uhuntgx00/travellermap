﻿using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;

namespace Maps.API
{
    public class JumpWorldsHandler : DataHandlerBase
    {
        public override string DefaultContentType { get { return System.Net.Mime.MediaTypeNames.Text.Xml; } }
        protected override string ServiceName { get { return "jumpworlds"; } }

        public override void Process(System.Web.HttpContext context)
        {
            // NOTE: This (re)initializes a static data structure used for 
            // resolving names into sector locations, so needs to be run
            // before any other objects (e.g. Worlds) are loaded.
            ResourceManager resourceManager = new ResourceManager(context.Server, context.Cache);

            //
            // Jump
            //
            int jump = Util.Clamp(GetIntOption(context, "jump", 6), 0, 12);

            //
            // Coordinates
            //
            SectorMap map = SectorMap.FromName(SectorMap.DefaultSetting, resourceManager);
            Location loc = new Location(map.FromName("Spinward Marches").Location, 1910);

            if (HasOption(context, "sector") && HasOption(context, "hex"))
            {
                string sectorName = GetStringOption(context, "sector");
                int hex = GetIntOption(context, "hex", 0);
                Sector sector = map.FromName(sectorName);
                if (sector == null)
                {
                    SendError(context.Response, 404, "Not Found", string.Format("The specified sector '{0}' was not found.", sectorName));
                    return;
                }
                loc = new Location(sector.Location, hex);
            }
            else if (HasOption(context, "sx") && HasOption(context, "sy") && HasOption(context, "hx") && HasOption(context, "hy"))
            {
                int sx = GetIntOption(context, "sx", 0);
                int sy = GetIntOption(context, "sy", 0);
                int hx = GetIntOption(context, "hx", 0);
                int hy = GetIntOption(context, "hy", 0);
                loc = new Location(map.FromLocation(sx, sy).Location, hx * 100 + hy);
            }
            else if (HasOption(context, "x") && HasOption(context, "y"))
            {
                loc = Astrometrics.CoordinatesToLocation(GetIntOption(context, "x", 0), GetIntOption(context, "y", 0));
            }

            Selector selector = new HexSelector(map, resourceManager, loc, jump);

            Result data = new Result();
            data.Worlds.AddRange(selector.Worlds);
            SendResult(context, data);
        }


        [XmlRoot(ElementName = "JumpWorlds")]
        // public for XML serialization
        public class Result
        {
            public Result()
            {
                Worlds = new List<World>();
            }

            [XmlElement("World")]
            public List<World> Worlds { get; set; }
        }
    }
}
