using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using GeePakEditor.Config;
using GeePakEditor.Models;
using GeePakEditor.Utils;

namespace GeePakEditor.Services;

/// <summary>
/// GEE 图片像素、zlib 载荷与标准 Bitmap 之间的转换服务。
/// </summary>
public sealed class PakImageCodec
{
    private readonly byte[] _palette = GeePaletteData.CreatePalette();

    /// <summary>
    /// 解压并解码指定 GEE 图片块。
    /// </summary>
    public Bitmap Decode(PakEntry entry)
    {
        if (entry.IsEmpty)
        {
            throw new InvalidOperationException("空槽位没有可解码的图片。");
        }

        var raw = ReadRawPayload(entry);
        return DecodeRaw(entry, raw);
    }

    /// <summary>
    /// 从常见图片文件创建 GEE 32 位 ARGB 图片块。
    /// </summary>
    public PakEntry EncodeFile(string imagePath, int index, short x = 0, short y = 0)
    {
        using var source = Image.FromFile(imagePath);
        if (source.Width is < 1 or > 4096 || source.Height is < 1 or > 4096)
        {
            throw new InvalidDataException($"图片尺寸 {source.Width}x{source.Height} 超出 GEE 支持范围 1..4096。");
        }

        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var raw = EncodeArgb32(bitmap);
        var compressed = Compress(raw);
        var useCompression = compressed.Length < raw.Length;
        return new PakEntry
        {
            Index = index,
            IsEmpty = false,
            ImageType = 7,
            Flags = 1,
            Width = bitmap.Width,
            Height = bitmap.Height,
            X = x,
            Y = y,
            RawSize = raw.Length,
            CompressedSize = useCompression ? compressed.Length : 0,
            Payload = useCompression ? compressed : raw,
            PlainHeader = new byte[GeePakConstants.ImageHeaderSize],
            SourceOffset = 0,
            IsModified = true
        };
    }

    /// <summary>
    /// 根据格式计算包含行对齐的原始像素数据长度。
    /// </summary>
    public static int CalculateRawSize(byte imageType, byte flags, int width, int height)
    {
        var rowSize = (imageType, flags) switch
        {
            (3, 0) => Align4(width),
            (5, 0) => Align4(checked(width * 2)),
            (6, 0) => Align4(checked(width * 3)),
            (6, 1) => checked(Align4(width * 3) + Align4(width)),
            (7, 0) or (7, 1) => checked(width * 4),
            _ => throw new InvalidDataException($"不支持的 GEE 图片布局：type={imageType}, flags={flags}。")
        };
        return checked(rowSize * height);
    }

    /// <summary>
    /// 解压图片载荷，并按归档声明的布局取得可见像素区。
    /// </summary>
    private static byte[] ReadRawPayload(PakEntry entry)
    {
        if (entry.CompressedSize == 0)
        {
            if (entry.PayloadMode == PakPayloadMode.M2Zip && entry.Payload.Length >= entry.RawSize)
            {
                // M2Zip 的读取器只消费按宽高计算出的可见像素前缀，保留载荷模式而不是散落长度特判。
                return entry.Payload.AsSpan(0, entry.RawSize).ToArray();
            }

            if (entry.Payload.Length != entry.RawSize)
            {
                throw new PakFormatException($"图片 {entry.Index} 原始载荷长度无效。");
            }

            return entry.Payload;
        }

        try
        {
            using var input = new MemoryStream(entry.Payload, false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(entry.RawSize);
            zlib.CopyTo(output);
            var raw = output.ToArray();
            if (entry.PayloadMode == PakPayloadMode.M2Zip && raw.Length >= entry.RawSize)
            {
                // M2Zip 物理流可能带有参考实现不会复制到纹理的辅助区，只交给像素解码器可见区。
                return raw.AsSpan(0, entry.RawSize).ToArray();
            }

            if (raw.Length != entry.RawSize)
            {
                throw new PakFormatException($"图片 {entry.Index} 解压后为 {raw.Length} 字节，预期 {entry.RawSize} 字节。");
            }

            return raw;
        }
        catch (InvalidDataException exception)
        {
            throw new PakFormatException($"图片 {entry.Index} 的 zlib 数据损坏。", exception);
        }
    }

    /// <summary>
    /// 把 GEE 底向上像素数据转换为标准 32 位 BGRA Bitmap。
    /// </summary>
    private Bitmap DecodeRaw(PakEntry entry, byte[] raw)
    {
        var target = new byte[checked(entry.Width * entry.Height * 4)];
        switch (entry.GetFormat())
        {
            case PakImageFormat.Palette8:
                DecodePalette(entry, raw, target);
                break;
            case PakImageFormat.R5G6B5:
                Decode565(entry, raw, target);
                break;
            case PakImageFormat.R8G8B8:
            case PakImageFormat.R8G8B8A8:
                DecodeRgb24(entry, raw, target);
                break;
            case PakImageFormat.X8R8G8B8:
            case PakImageFormat.A8R8G8B8:
                DecodeArgb32(entry, raw, target);
                break;
            default:
                throw new InvalidDataException($"图片 {entry.Index} 的像素格式不受支持。");
        }

        var bitmap = new Bitmap(entry.Width, entry.Height, PixelFormat.Format32bppArgb);
        var rectangle = new Rectangle(0, 0, entry.Width, entry.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < entry.Height; row++)
            {
                Marshal.Copy(target, row * entry.Width * 4, data.Scan0 + row * data.Stride, entry.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// 解码固定调色板索引。
    /// </summary>
    private void DecodePalette(PakEntry entry, byte[] raw, byte[] target)
    {
        var stride = Align4(entry.Width);
        for (var y = 0; y < entry.Height; y++)
        {
            var sourceY = entry.Height - 1 - y;
            for (var x = 0; x < entry.Width; x++)
            {
                var paletteOffset = raw[sourceY * stride + x] * 4;
                var targetOffset = (y * entry.Width + x) * 4;
                target[targetOffset] = _palette[paletteOffset];
                target[targetOffset + 1] = _palette[paletteOffset + 1];
                target[targetOffset + 2] = _palette[paletteOffset + 2];
                target[targetOffset + 3] = _palette[paletteOffset + 3];
            }
        }
    }

    /// <summary>
    /// 解码底向上的 R5G6B5 像素。
    /// </summary>
    private static void Decode565(PakEntry entry, byte[] raw, byte[] target)
    {
        var stride = Align4(checked(entry.Width * 2));
        for (var y = 0; y < entry.Height; y++)
        {
            var sourceY = entry.Height - 1 - y;
            for (var x = 0; x < entry.Width; x++)
            {
                var value = PakBinary.ReadUInt16(raw, sourceY * stride + x * 2);
                var red = (value >> 11) & 31;
                var green = (value >> 5) & 63;
                var blue = value & 31;
                var targetOffset = (y * entry.Width + x) * 4;
                target[targetOffset] = (byte)((blue << 3) | (blue >> 2));
                target[targetOffset + 1] = (byte)((green << 2) | (green >> 4));
                target[targetOffset + 2] = (byte)((red << 3) | (red >> 2));
                target[targetOffset + 3] = 255;
            }
        }
    }

    /// <summary>
    /// 解码底向上的 24 位 BGR 颜色与可选独立 Alpha 平面。
    /// </summary>
    private static void DecodeRgb24(PakEntry entry, byte[] raw, byte[] target)
    {
        var colorStride = Align4(checked(entry.Width * 3));
        var alphaStride = Align4(entry.Width);
        var alphaOffset = checked(colorStride * entry.Height);
        for (var y = 0; y < entry.Height; y++)
        {
            var sourceY = entry.Height - 1 - y;
            for (var x = 0; x < entry.Width; x++)
            {
                var sourceOffset = sourceY * colorStride + x * 3;
                var targetOffset = (y * entry.Width + x) * 4;
                target[targetOffset] = raw[sourceOffset];
                target[targetOffset + 1] = raw[sourceOffset + 1];
                target[targetOffset + 2] = raw[sourceOffset + 2];
                target[targetOffset + 3] = entry.Flags == 1 ? raw[alphaOffset + sourceY * alphaStride + x] : (byte)255;
            }
        }
    }

    /// <summary>
    /// 解码底向上的 32 位 BGRA 像素。
    /// </summary>
    private static void DecodeArgb32(PakEntry entry, byte[] raw, byte[] target)
    {
        var stride = checked(entry.Width * 4);
        for (var y = 0; y < entry.Height; y++)
        {
            var sourceY = entry.Height - 1 - y;
            Buffer.BlockCopy(raw, sourceY * stride, target, y * stride, stride);
            if (entry.Flags == 0)
            {
                for (var x = 0; x < entry.Width; x++)
                {
                    target[(y * entry.Width + x) * 4 + 3] = 255;
                }
            }
        }
    }

    /// <summary>
    /// 把 Bitmap 编码为 GEE 使用的底向上 BGRA 数据。
    /// </summary>
    private static byte[] EncodeArgb32(Bitmap bitmap)
    {
        var stride = checked(bitmap.Width * 4);
        var raw = new byte[checked(stride * bitmap.Height)];
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBuffer = new byte[stride];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, rowBuffer, 0, stride);
                Buffer.BlockCopy(rowBuffer, 0, raw, (bitmap.Height - 1 - y) * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return raw;
    }

    /// <summary>
    /// 使用标准 zlib 数据流压缩新导入图片。
    /// </summary>
    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>
    /// 将行字节数向上对齐到四字节边界。
    /// </summary>
    private static int Align4(int value) => checked((value + 3) & ~3);
}
