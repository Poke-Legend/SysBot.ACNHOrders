using System;
using System.IO;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SysBot.ACNHOrders
{
    public class DodoDraw
    {
        private readonly string ImagePathTemplate = "dodo.png";
        private readonly string FontPath = "dodo.ttf";
        private readonly string ImagePathOutput;
        private readonly Font DodoFont;
        private readonly Image<Rgba32> BaseImage;

        public DodoDraw(float fontPercentage = 100)
        {
            var fontCollection = new FontCollection();
            var dodoFontFamily = fontCollection.Add(FontPath);
            BaseImage = Image.Load<Rgba32>(ImagePathTemplate);
            DodoFont = dodoFontFamily.CreateFont(BaseImage.Height * 0.4f * (fontPercentage / 100f), FontStyle.Regular);
            ImagePathOutput = $"current_{ImagePathTemplate}";
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

        public string? GetProcessedDodoImagePath() => File.Exists(ImagePathOutput) ? ImagePathOutput : null;
    }
}
