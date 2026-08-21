[CmdletBinding()]
param(
    [string]$Source
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $projectRoot 'assets\app-icon-source.png'
}
$master = Join-Path $projectRoot 'assets\app-icon-master.png'
$windowsAssets = Join-Path $projectRoot 'src\DeezerRpc.Windows\Assets'
$androidRoot = Join-Path $projectRoot 'src\DeezerRpc.Android\Resources'
New-Item -ItemType Directory -Force -Path $windowsAssets | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class DeezerRpcIconPreparer
{
    public static void ExtractExteriorAndResize(string source, string destination, int size)
    {
        using (var original = new Bitmap(source))
        using (var working = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(working))
            {
                graphics.DrawImageUnscaled(original, 0, 0);
            }

            RemoveConnectedNeutralBackground(working);
            using (var resized = Resize(working, size))
            {
                resized.Save(destination, ImageFormat.Png);
            }
        }
    }

    public static void ResizePng(string source, string destination, int size)
    {
        using (var image = new Bitmap(source))
        using (var resized = Resize(image, size))
        {
            resized.Save(destination, ImageFormat.Png);
        }
    }

    public static void WriteIco(string source, string destination, int[] sizes)
    {
        using (var image = new Bitmap(source))
        using (var stream = File.Create(destination))
        using (var writer = new BinaryWriter(stream))
        {
            var frames = new List<byte[]>();
            foreach (var size in sizes)
            {
                using (var resized = Resize(image, size))
                using (var memory = new MemoryStream())
                {
                    resized.Save(memory, ImageFormat.Png);
                    frames.Add(memory.ToArray());
                }
            }

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Count);
            var offset = 6 + (16 * frames.Count);
            for (var index = 0; index < frames.Count; index++)
            {
                var size = sizes[index];
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frames[index].Length);
                writer.Write(offset);
                offset += frames[index].Length;
            }

            foreach (var frame in frames)
            {
                writer.Write(frame);
            }
        }
    }

    private static Bitmap Resize(Bitmap source, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        result.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
        }
        return result;
    }

    private static void RemoveConnectedNeutralBackground(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var count = bitmap.Width * bitmap.Height;
            var pixels = new int[count];
            Marshal.Copy(data.Scan0, pixels, 0, count);
            var exterior = new BitArray(count);
            var queue = new Queue<int>();

            Action<int> add = index =>
            {
                if (!exterior[index] && IsExteriorCandidate(pixels[index]))
                {
                    exterior[index] = true;
                    queue.Enqueue(index);
                }
            };

            for (var x = 0; x < bitmap.Width; x++)
            {
                add(x);
                add(((bitmap.Height - 1) * bitmap.Width) + x);
            }
            for (var y = 0; y < bitmap.Height; y++)
            {
                add(y * bitmap.Width);
                add((y * bitmap.Width) + bitmap.Width - 1);
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % bitmap.Width;
                if (x > 0) add(index - 1);
                if (x + 1 < bitmap.Width) add(index + 1);
                if (index >= bitmap.Width) add(index - bitmap.Width);
                if (index + bitmap.Width < count) add(index + bitmap.Width);
            }

            for (var index = 0; index < count; index++)
            {
                if (exterior[index]) pixels[index] &= 0x00FFFFFF;
            }
            Marshal.Copy(pixels, 0, data.Scan0, count);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static bool IsExteriorCandidate(int pixel)
    {
        var blue = pixel & 255;
        var green = (pixel >> 8) & 255;
        var red = (pixel >> 16) & 255;
        var minimum = Math.Min(red, Math.Min(green, blue));
        var maximum = Math.Max(red, Math.Max(green, blue));
        return minimum >= 28 && maximum - minimum <= 28;
    }
}
'@

[DeezerRpcIconPreparer]::ExtractExteriorAndResize($Source, $master, 1024)
[DeezerRpcIconPreparer]::WriteIco(
    $master,
    (Join-Path $windowsAssets 'app-icon.ico'),
    [int[]]@(16, 20, 24, 32, 40, 48, 64, 128, 256))
[DeezerRpcIconPreparer]::ResizePng($master, (Join-Path $windowsAssets 'app-icon.png'), 256)

$androidSizes = @{
    'mipmap-mdpi' = 48
    'mipmap-hdpi' = 72
    'mipmap-xhdpi' = 96
    'mipmap-xxhdpi' = 144
    'mipmap-xxxhdpi' = 192
}
foreach ($entry in $androidSizes.GetEnumerator()) {
    $directory = Join-Path $androidRoot $entry.Key
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    [DeezerRpcIconPreparer]::ResizePng($master, (Join-Path $directory 'appicon.png'), $entry.Value)
}

Write-Host "Icônes générées depuis $Source"
