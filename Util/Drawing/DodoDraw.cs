using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace SysBot.ACNHOrders
{
    public class DodoDraw
    {
        private string ImagePathTemplate { get; set; } = "dodo.png";
        private string FontPath { get; set; } = "dodo.ttf";
        private string ImagePathOutput => "current_" + ImagePathTemplate;

        private readonly FontCollection FontCollection = new();
        private readonly FontFamily DodoFontFamily;
        private readonly Font DodoFont;
        private readonly Image<Rgba32> BaseImage;

        public DodoDraw(float fontPercentage = 100)
        {
            DodoFontFamily = FontCollection.Add(FontPath);
            BaseImage = Image.Load<Rgba32>(ImagePathTemplate);
            DodoFont = DodoFontFamily.CreateFont(BaseImage.Height * 0.4f * (fontPercentage / 100f), FontStyle.Regular);
        }

        public string Draw(string dodo)
        {
            var textOptions = new TextOptions(DodoFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var drawingOptions = new DrawingOptions
            {
                GraphicsOptions = new GraphicsOptions
                {
                    Antialias = true,
                    AntialiasSubpixelDepth = 16
                }
            };

            var textPosition = new PointF(BaseImage.Width * 0.5f, BaseImage.Height * 0.38f);

            using (var img = BaseImage.Clone(ctx => ctx.DrawText(drawingOptions, dodo, DodoFont, Color.White, textPosition)))
            {
                img.Save(ImagePathOutput);
            }

            return ImagePathOutput;
        }

        public string? GetProcessedDodoImagePath()
        {
            if (File.Exists(ImagePathOutput))
                return ImagePathOutput;

            return null;
        }
    }
}
