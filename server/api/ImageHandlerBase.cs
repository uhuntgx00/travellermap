using Maps.Rendering;
using Maps.Serialization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net.Mime;
using System.Web;

namespace Maps.API
{
    public abstract class ImageHandlerBase : DataHandlerBase
    {
        public override string DefaultContentType { get { return Util.MediaTypeName_Image_Png; } }

        public const double MinScale = 0.0078125; // Math.Pow(2, -7);
        public const double MaxScale = 512; // Math.Pow(2, 9);

        protected void ProduceResponse(HttpContext context, string title, Render.RenderContext ctx, Size tileSize,
            int rot = 0, float translateX = 0, float translateY = 0,
            bool transparent = false)
        {
            ProduceResponse(context, this, title, ctx, tileSize, rot, translateX, translateY, transparent,
                (context.Items["RouteData"] as System.Web.Routing.RouteData).Values);
        }

        protected static void ProduceResponse(HttpContext context, ITypeAccepter accepter, string title, Render.RenderContext ctx, Size tileSize,
            int rot = 0, float translateX = 0, float translateY = 0,
            bool transparent = false, IDictionary<string, Object> queryDefaults = null)
        {
            // New-style Options
            // TODO: move to ParseOptions (maybe - requires options to be parsed after stylesheet creation?)
            if (HandlerBase.GetBoolOption(context.Request, "sscoords", queryDefaults: queryDefaults, defaultValue: false))
                ctx.styles.hexCoordinateStyle = Stylesheet.HexCoordinateStyle.Subsector;

            if (HandlerBase.GetBoolOption(context.Request, "allhexes", queryDefaults: queryDefaults, defaultValue: false))
                ctx.styles.numberAllHexes = true;

            if (!HandlerBase.GetBoolOption(context.Request, "routes", queryDefaults: queryDefaults, defaultValue: true))
            {
                ctx.styles.macroRoutes.visible = false;
                ctx.styles.microRoutes.visible = false;
            }

            ctx.styles.dimUnofficialSectors = HandlerBase.GetBoolOption(context.Request, "dimunofficial", queryDefaults: queryDefaults, defaultValue: false);

            double devicePixelRatio = HandlerBase.GetDoubleOption(context.Request, "dpr", defaultValue: 1, queryDefaults: queryDefaults);
            if (devicePixelRatio <= 0)
                devicePixelRatio = 1;

            if (accepter.Accepts(context, MediaTypeNames.Application.Pdf))
            {
                using (var document = new PdfDocument())
                {
                    document.Version = 14; // 1.4 for opacity
                    document.Info.Title = title;
                    document.Info.Author = "Joshua Bell";
                    document.Info.Creator = "TravellerMap.com";
                    document.Info.Subject = DateTime.Now.ToString("F", CultureInfo.InvariantCulture);
                    document.Info.Keywords = "The Traveller game in all forms is owned by Far Future Enterprises. Copyright (C) 1977 - 2015 Far Future Enterprises. Traveller is a registered trademark of Far Future Enterprises.";

                    // TODO: Credits/Copyright
                    // This is close, but doesn't define the namespace correctly:
                    // document.Info.Elements.Add( new KeyValuePair<string, PdfItem>( "/photoshop/Copyright", new PdfString( "HelloWorld" ) ) );

                    PdfPage page = document.AddPage();

                    // NOTE: only PageUnit currently supported in XGraphics is Points
                    page.Width = XUnit.FromPoint(tileSize.Width);
                    page.Height = XUnit.FromPoint(tileSize.Height);

                    PdfSharp.Drawing.XGraphics gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);

                    RenderToGraphics(ctx, rot, translateX, translateY, gfx);

                    using (var stream = new MemoryStream())
                    {
                        document.Save(stream, closeStream: false);

                        context.Response.ContentType = MediaTypeNames.Application.Pdf;
                        context.Response.AddHeader("content-length", stream.Length.ToString());
                        context.Response.AddHeader("content-disposition", "inline;filename=\"map.pdf\"");
                        context.Response.BinaryWrite(stream.ToArray());
                        context.Response.Flush();
                        context.Response.Close();
                    }

                    return;
                }
            }

            using (var bitmap = new Bitmap((int)Math.Floor(tileSize.Width * devicePixelRatio), (int)Math.Floor(tileSize.Height * devicePixelRatio), PixelFormat.Format32bppArgb))
            {
                if (transparent)
                    bitmap.MakeTransparent();

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    using (var graphics = XGraphics.FromGraphics(g, new XSize(tileSize.Width * devicePixelRatio, tileSize.Height * devicePixelRatio)))
                    {
                        graphics.ScaleTransform(devicePixelRatio);

                        RenderToGraphics(ctx, rot, translateX, translateY, graphics);
                    }
                }

                bool dataURI = HandlerBase.GetBoolOption(context.Request, "datauri", queryDefaults: queryDefaults, defaultValue: false );
                MemoryStream ms = null;
                if (dataURI)
                    ms = new MemoryStream();

                BitmapResponse(context.Response, dataURI ? ms : context.Response.OutputStream, ctx.styles, bitmap, transparent ? Util.MediaTypeName_Image_Png : null);

                if (dataURI)
                {
                    string contentType = context.Response.ContentType;
                    context.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
                    ms.Seek(0, SeekOrigin.Begin);

                    context.Response.Output.Write("data:");
                    context.Response.Output.Write(contentType);
                    context.Response.Output.Write(";base64,");
                    context.Response.Output.Flush();

                    byte[] buffer = new byte[4096];
                    System.Security.Cryptography.ICryptoTransform transform = new System.Security.Cryptography.ToBase64Transform();
                    using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(context.Response.OutputStream, transform, System.Security.Cryptography.CryptoStreamMode.Write))
                    {
                        int bytesRead;
                        while ((bytesRead = ms.Read(buffer, 0, buffer.Length)) > 0)
                            cs.Write(buffer, 0, bytesRead);
                        cs.FlushFinalBlock();
                    }
                    context.Response.OutputStream.Flush();
                }
            }
        }

        private static void RenderToGraphics(Render.RenderContext ctx, int rot, float translateX, float translateY, XGraphics graphics)
        {
            graphics.TranslateTransform(translateX, translateY);
            graphics.RotateTransform(rot * 90);

            if (ctx.border && ctx.clipPath != null)
            {
                using (Maps.Rendering.RenderUtil.SaveState(graphics))
                {
                    // Render border in world space
                    XMatrix m = ctx.ImageSpaceToWorldSpace;
                    graphics.MultiplyTransform(m);
                    XPen pen = new XPen(ctx.styles.imageBorderColor, 0.2f);

                    // PdfSharp can't ExcludeClip so we take advantage of the fact that we know
                    // the path starts on the left edge and proceeds clockwise. We extend the
                    // path with a counterclockwise border around it, then use that to exclude
                    // the original path's region for rendering the border.
                    ctx.clipPath.Flatten();
                    RectangleF bounds = PathUtil.Bounds(ctx.clipPath);
                    bounds.Inflate(2 * (float)pen.Width, 2 * (float)pen.Width);
                    List<byte> types = new List<byte>(ctx.clipPath.Internals.GdiPath.PathTypes);
                    List<PointF> points = new List<PointF>(ctx.clipPath.Internals.GdiPath.PathPoints);

                    PointF key = points[0];
                    points.Add(new PointF(bounds.Left, key.Y)); types.Add(1);
                    points.Add(new PointF(bounds.Left, bounds.Bottom)); types.Add(1);
                    points.Add(new PointF(bounds.Right, bounds.Bottom)); types.Add(1);
                    points.Add(new PointF(bounds.Right, bounds.Top)); types.Add(1);
                    points.Add(new PointF(bounds.Left, bounds.Top)); types.Add(1);
                    points.Add(new PointF(bounds.Left, key.Y)); types.Add(1);
                    points.Add(new PointF(key.X, key.Y)); types.Add(1);

                    XGraphicsPath path = new XGraphicsPath(points.ToArray(), types.ToArray(), XFillMode.Winding);
                    graphics.IntersectClip(path);
                    graphics.DrawPath(pen, ctx.clipPath);
                }
            }

            using (Maps.Rendering.RenderUtil.SaveState(graphics))
            {
                /*
                if (ctx.clipPath != null)
                {
                    XMatrix m = ctx.ImageSpaceToWorldSpace;
                    graphics.MultiplyTransform(m);
                    graphics.IntersectClip(ctx.clipPath);
                    m.Invert();
                    graphics.MultiplyTransform(m);
                }
                 * */

                ctx.graphics = graphics;
                Maps.Rendering.Render.RenderTile(ctx);
            }


        }

        private static void BitmapResponse(HttpResponse response, Stream outputStream, Stylesheet styles, Bitmap bitmap, string mimeType)
        {
            // JPEG or PNG if not specified, based on style
            mimeType = mimeType ?? styles.preferredMimeType;

            response.ContentType = mimeType;

            // Searching for a matching encoder
            ImageCodecInfo encoder = null;
            ImageCodecInfo[] encoders = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < encoders.Length; ++i)
            {
                if (encoders[i].MimeType == response.ContentType)
                {
                    encoder = encoders[i];
                    break;
                }
            }

            if (encoder != null)
            {
                EncoderParameters encoderParams;
                if (mimeType == MediaTypeNames.Image.Jpeg)
                {
                    encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)95);
                }
                else if (mimeType == Util.MediaTypeName_Image_Png)
                {
                    encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.ColorDepth, 8);
                }
                else
                {
                    encoderParams = new EncoderParameters(0);
                }

                if (mimeType == Util.MediaTypeName_Image_Png)
                {
                    // PNG encoder is picky about streams - need to do an indirection
                    // http://www.west-wind.com/WebLog/posts/8230.aspx
                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, encoder, encoderParams);
                        ms.WriteTo(outputStream);
                    }
                }
                else
                {
                    bitmap.Save(outputStream, encoder, encoderParams);
                }

                encoderParams.Dispose();
            }
            else
            {
                // Default to GIF if we can't find anything
                response.ContentType = MediaTypeNames.Image.Gif;
                bitmap.Save(outputStream, ImageFormat.Gif);
            }
        }

        protected static Sector GetPostedSector(HttpRequest request, ErrorLogger errors)
        {
            Sector sector = null;

            if (request.Files["file"] != null && request.Files["file"].ContentLength > 0)
            {
                HttpPostedFile hpf = request.Files["file"];
                sector = new Sector(hpf.InputStream, hpf.ContentType, errors);
            }
            else if (!String.IsNullOrEmpty(request.Form["data"]))
            {
                string data = request.Form["data"];
                sector = new Sector(data.ToStream(), MediaTypeNames.Text.Plain, errors);
            }
            else if (new ContentType(request.ContentType).MediaType == MediaTypeNames.Text.Plain)
            {
                sector = new Sector(request.InputStream, MediaTypeNames.Text.Plain, errors);
            }
            else
            {
                return null;
            }

            if (request.Files["metadata"] != null && request.Files["metadata"].ContentLength > 0)
            {
                HttpPostedFile hpf = request.Files["metadata"];

                string type = SectorMetadataFileParser.SniffType(hpf.InputStream);
                Sector meta = SectorMetadataFileParser.ForType(type).Parse(hpf.InputStream);
                sector.Merge(meta);
            }
            else if (!String.IsNullOrEmpty(request.Form["metadata"]))
            {
                string metadata = request.Form["metadata"];
                string type = SectorMetadataFileParser.SniffType(metadata.ToStream());
                var parser = SectorMetadataFileParser.ForType(type);
                using (var reader = new StringReader(metadata))
                {
                    Sector meta = parser.Parse(reader);
                    sector.Merge(meta);
                }
            }

            return sector;
        }
    }
}
