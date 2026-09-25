using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class GenerateIcons
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static void Main(string[] args)
    {
        string outputFolder = args.Length > 0 ? args[0] : "assets";
        Directory.CreateDirectory(outputFolder);
        WriteIco(Path.Combine(outputFolder, "MacRando.ico"), false);
        WriteIco(Path.Combine(outputFolder, "MacRandoTray.ico"), true);
        Console.WriteLine("Generated MacRando.ico and MacRandoTray.ico");
    }

    private static void WriteIco(string path, bool tray)
    {
        var images = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using (Bitmap bitmap = Render(size, tray))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                images.Add(stream.ToArray());
            }
        }

        using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);
            int offset = 6 + (16 * images.Count);
            for (int index = 0; index < images.Count; index++)
            {
                int size = Sizes[index];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[index].Length);
                writer.Write(offset);
                offset += images[index].Length;
            }
            foreach (byte[] image in images)
            {
                writer.Write(image);
            }
        }
    }

    private static Bitmap Render(int size, bool tray)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            float scale = size / 256F;

            if (!tray)
            {
                Rectangle bounds = new Rectangle(4, 4, 248, 248);
                using (GraphicsPath path = RoundedRectangle(bounds, 52 * scale))
                using (LinearGradientBrush background = new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(30, 64, 175),
                    Color.FromArgb(14, 165, 233),
                    LinearGradientMode.Vertical))
                using (Pen border = new Pen(Color.FromArgb(125, 211, 252), 5 * scale))
                {
                    graphics.FillPath(background, path);
                    graphics.DrawPath(border, path);
                }
            }

            Point a = new Point((int)(64 * scale), (int)(174 * scale));
            Point b = new Point((int)(128 * scale), (int)(91 * scale));
            Point c = new Point((int)(193 * scale), (int)(174 * scale));
            float lineWidth = Math.Max(1.5F, 10 * scale);
            using (Pen glow = new Pen(tray ? Color.FromArgb(80, 125, 211, 252) : Color.FromArgb(100, 125, 211, 252), lineWidth * 2.4F))
            using (Pen line = new Pen(tray ? Color.White : Color.FromArgb(224, 247, 255), lineWidth))
            {
                graphics.DrawLine(glow, a, b);
                graphics.DrawLine(glow, b, c);
                graphics.DrawLine(line, a, b);
                graphics.DrawLine(line, b, c);
            }

            DrawNode(graphics, a, 27 * scale, tray);
            DrawNode(graphics, b, 31 * scale, tray);
            DrawNode(graphics, c, 27 * scale, tray);

            if (!tray)
            {
                using (SolidBrush status = new SolidBrush(Color.FromArgb(74, 222, 128)))
                {
                    float radius = 12 * scale;
                    graphics.FillEllipse(status, 196 * scale, 190 * scale, radius, radius);
                }
            }
        }
        return bitmap;
    }

    private static void DrawNode(Graphics graphics, Point center, float diameter, bool tray)
    {
        float radius = diameter / 2F;
        Rectangle bounds = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)diameter, (int)diameter);
        using (SolidBrush shadow = new SolidBrush(tray ? Color.FromArgb(80, 15, 23, 42) : Color.FromArgb(100, 15, 23, 42)))
        using (SolidBrush outer = new SolidBrush(tray ? Color.FromArgb(220, 37, 99, 235) : Color.FromArgb(30, 64, 175)))
        using (SolidBrush inner = new SolidBrush(tray ? Color.White : Color.FromArgb(125, 211, 252)))
        using (Pen rim = new Pen(Color.White, Math.Max(1F, diameter * 0.07F)))
        {
            Rectangle shadowBounds = Rectangle.Inflate(bounds, (int)Math.Max(1F, diameter * 0.12F), (int)Math.Max(1F, diameter * 0.12F));
            graphics.FillEllipse(shadow, shadowBounds);
            graphics.FillEllipse(outer, bounds);
            graphics.FillEllipse(inner, Rectangle.Inflate(bounds, -(int)Math.Max(2F, diameter * 0.22F), -(int)Math.Max(2F, diameter * 0.22F)));
            graphics.DrawEllipse(rim, bounds);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, float radius)
    {
        float diameter = Math.Max(1F, radius * 2F);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, (int)diameter, (int)diameter, 180, 90);
        path.AddArc(bounds.Right - (int)diameter, bounds.Top, (int)diameter, (int)diameter, 270, 90);
        path.AddArc(bounds.Right - (int)diameter, bounds.Bottom - (int)diameter, (int)diameter, (int)diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - (int)diameter, (int)diameter, (int)diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
