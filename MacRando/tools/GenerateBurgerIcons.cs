using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class GenerateBurgerIcons
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };
    private const int SourceSize = 512;

    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException("Usage: GenerateBurgerIcons.exe <output-folder> <source-image>");
        }

        string outputFolder = args[0];
        string sourcePath = args[1];
        Directory.CreateDirectory(outputFolder);
        Bitmap prepared = PrepareSource(sourcePath);
        try
        {
            prepared.Save(Path.Combine(outputFolder, "MacRando-icon.png"), ImageFormat.Png);
            WriteIco(Path.Combine(outputFolder, "MacRando.ico"), prepared);
            WriteIco(Path.Combine(outputFolder, "MacRandoTray.ico"), prepared);
            Console.WriteLine("Generated burger app and tray icons from " + sourcePath);
        }
        finally
        {
            prepared.Dispose();
        }
    }

    private static Bitmap PrepareSource(string path)
    {
        using (Image original = Image.FromFile(path))
        using (var full = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(full))
            {
                graphics.DrawImage(original, 0, 0, original.Width, original.Height);
            }
            RemoveConnectedWhiteBackground(full);
            Rectangle content = FindContentBounds(full);
            int padding = Math.Max(8, Math.Max(content.Width, content.Height) / 18);
            content = Rectangle.Inflate(content, padding, padding);
            content.Intersect(new Rectangle(0, 0, full.Width, full.Height));

            var result = new Bitmap(SourceSize, SourceSize, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Min((SourceSize - 16F) / content.Width, (SourceSize - 16F) / content.Height);
                int width = Math.Max(1, (int)Math.Round(content.Width * scale));
                int height = Math.Max(1, (int)Math.Round(content.Height * scale));
                int x = (SourceSize - width) / 2;
                int y = (SourceSize - height) / 2;
                graphics.DrawImage(full, new Rectangle(x, y, width, height), content, GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    private static void RemoveConnectedWhiteBackground(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        bool[] visited = new bool[width * height];
        var pending = new Stack<int>();
        for (int x = 0; x < width; x++)
        {
            EnqueueWhite(bitmap, pending, visited, x, 0, width, height);
            EnqueueWhite(bitmap, pending, visited, x, height - 1, width, height);
        }
        for (int y = 0; y < height; y++)
        {
            EnqueueWhite(bitmap, pending, visited, 0, y, width, height);
            EnqueueWhite(bitmap, pending, visited, width - 1, y, width, height);
        }

        while (pending.Count > 0)
        {
            int index = pending.Pop();
            int x = index % width;
            int y = index / width;
            if (x > 0) EnqueueWhite(bitmap, pending, visited, x - 1, y, width, height);
            if (x + 1 < width) EnqueueWhite(bitmap, pending, visited, x + 1, y, width, height);
            if (y > 0) EnqueueWhite(bitmap, pending, visited, x, y - 1, width, height);
            if (y + 1 < height) EnqueueWhite(bitmap, pending, visited, x, y + 1, width, height);
        }
    }

    private static void EnqueueWhite(Bitmap bitmap, Stack<int> pending, bool[] visited, int x, int y, int width, int height)
    {
        int index = y * width + x;
        if (visited[index]) return;
        Color color = bitmap.GetPixel(x, y);
        if (color.A < 250 || color.R < 245 || color.G < 245 || color.B < 245) return;
        visited[index] = true;
        bitmap.SetPixel(x, y, Color.FromArgb(0, color.R, color.G, color.B));
        pending.Push(index);
    }

    private static Rectangle FindContentBounds(Bitmap bitmap)
    {
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 8)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }
        if (right < left || bottom < top) throw new InvalidDataException("The source image contains no visible artwork.");
        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static void WriteIco(string path, Bitmap source)
    {
        var images = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using (Bitmap bitmap = Render(source, size))
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
            int offset = 6 + 16 * images.Count;
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
            foreach (byte[] image in images) writer.Write(image);
        }
    }

    private static Bitmap Render(Bitmap source, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
        }
        return bitmap;
    }
}
