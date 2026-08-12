using System.IO;
using GeePakEditor.Config;
using GeePakEditor.Models;
using GeePakEditor.Utils;

namespace GeePakEditor.Services;

/// <summary>
/// 读取传统 WZL 数据文件及同名 WZX 索引文件的只读服务。
/// </summary>
internal sealed class WzlArchiveReader
{
    /// <summary>
    /// WZX 文件前 48 字节为保留头，随后是与逻辑索引一一对应的 32 位 WZL 块偏移。
    /// </summary>
    private const int WzxHeaderSize = 48;

    /// <summary>
    /// WZL 文件前 64 字节为保留头，真实图片块从索引偏移处开始。
    /// </summary>
    private const int WzlHeaderSize = 64;

    /// <summary>
    /// 打开 WZL/WZX 只读归档，并转换为现有预览链路可识别的图片槽位。
    /// </summary>
    /// <param name="filePath">用户选择的 WZL 文件路径。</param>
    /// <returns>只读 WZL 归档。</returns>
    public PakArchive Open(string filePath)
    {
        var resolvedWzlPath = Path.GetFullPath(filePath);
        var resolvedWzxPath = ResolveWzxPath(resolvedWzlPath);
        var data = File.ReadAllBytes(resolvedWzlPath);
        var offsets = ReadIndexOffsets(resolvedWzxPath);
        var slots = ReadSlots(data, offsets);

        return new PakArchive
        {
            FilePath = resolvedWzlPath,
            Title = "WZL/WZX 只读资源",
            Format = PakArchiveFormat.Wzl,
            CanWrite = false,
            Slots = slots
        };
    }

    /// <summary>
    /// 按 WZL 路径寻找同目录同名 WZX 索引，兼容大小写不同的扩展名。
    /// </summary>
    /// <param name="wzlPath">WZL 数据文件完整路径。</param>
    /// <returns>实际存在的 WZX 索引文件路径。</returns>
    private static string ResolveWzxPath(string wzlPath)
    {
        var directory = Path.GetDirectoryName(wzlPath)
            ?? throw new PakFormatException("WZL 路径没有有效目录。");
        var baseName = Path.GetFileNameWithoutExtension(wzlPath);
        var directPath = Path.Combine(directory, $"{baseName}.wzx");
        if (File.Exists(directPath))
        {
            return directPath;
        }

        var matchedPath = Directory.EnumerateFiles(directory)
            .FirstOrDefault(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(path), ".wzx", StringComparison.OrdinalIgnoreCase));
        return matchedPath ?? throw new PakFormatException($"未找到同名 WZX 索引文件：{directPath}");
    }

    /// <summary>
    /// 读取 WZX 头部中的槽位数量，并解析后续偏移表。
    /// </summary>
    /// <param name="wzxPath">WZX 索引文件完整路径。</param>
    /// <returns>按逻辑索引排列的 WZL 块偏移数组。</returns>
    private static uint[] ReadIndexOffsets(string wzxPath)
    {
        var data = File.ReadAllBytes(wzxPath);
        if (data.Length < WzxHeaderSize || (data.Length - WzxHeaderSize) % sizeof(uint) != 0)
        {
            throw new PakFormatException("WZX 索引文件长度无效。");
        }

        var headerSlotCount = PakBinary.ReadUInt32(data, WzxHeaderSize - sizeof(uint));
        var tableSlotCount = checked((data.Length - WzxHeaderSize) / sizeof(uint));
        if (headerSlotCount != checked((uint)tableSlotCount))
        {
            throw new PakFormatException($"WZX 索引数量不一致：头部={headerSlotCount}, 表={tableSlotCount}。");
        }

        if (tableSlotCount > GeePakConstants.MaximumSlotCount)
        {
            throw new PakFormatException($"WZX 槽位数量超过上限 {GeePakConstants.MaximumSlotCount:N0}。");
        }

        var offsets = new uint[tableSlotCount];
        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] = PakBinary.ReadUInt32(data, WzxHeaderSize + index * sizeof(uint));
        }

        return offsets;
    }

    /// <summary>
    /// 按 WZX 偏移表读取 WZL 中的全部非空图片块，并保留空槽位。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="offsets">WZX 中读取出的逻辑槽位偏移。</param>
    /// <returns>按逻辑索引排列的图片槽位。</returns>
    private static List<PakEntry> ReadSlots(byte[] data, IReadOnlyList<uint> offsets)
    {
        if (data.Length < WzlHeaderSize)
        {
            throw new PakFormatException("WZL 文件长度小于固定头长度。");
        }

        var occupied = new List<(int Index, uint Offset)>();
        var seenOffsets = new HashSet<uint>();
        for (var index = 0; index < offsets.Count; index++)
        {
            var offset = offsets[index];
            if (offset == 0)
            {
                continue;
            }

            if (offset < WzlHeaderSize || offset > data.Length - GeePakConstants.ImageHeaderSize)
            {
                throw new PakFormatException($"WZL 图片 {index} 的块头偏移 {offset} 越界。");
            }

            if (!seenOffsets.Add(offset))
            {
                throw new PakFormatException($"WZL 图片 {index} 与其他槽位使用了重复块偏移 {offset}。");
            }

            occupied.Add((index, offset));
        }

        occupied.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        var slots = Enumerable.Range(0, offsets.Count).Select(CreateEmptyEntry).ToList();
        for (var order = 0; order < occupied.Count; order++)
        {
            var item = occupied[order];
            var nextOffset = order + 1 < occupied.Count ? occupied[order + 1].Offset : checked((uint)data.Length);
            slots[item.Index] = ReadEntry(data, item.Index, item.Offset, nextOffset);
        }

        return slots;
    }

    /// <summary>
    /// 读取单个 WZL 图片块头和载荷，并验证压缩长度与块边界。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="index">图片逻辑索引。</param>
    /// <param name="offset">图片块在 WZL 中的起始偏移。</param>
    /// <param name="nextOffset">物理顺序中的下一个图片块偏移。</param>
    /// <returns>可交给图片解码器的槽位。</returns>
    private static PakEntry ReadEntry(byte[] data, int index, uint offset, uint nextOffset)
    {
        var header = data.AsSpan(checked((int)offset), GeePakConstants.ImageHeaderSize).ToArray();
        var imageType = header[0];
        var flags = header[3];
        var width = PakBinary.ReadUInt16(header, 4);
        var height = PakBinary.ReadUInt16(header, 6);
        var x = PakBinary.ReadInt16(header, 8);
        var y = PakBinary.ReadInt16(header, 10);
        var compressedSizeValue = PakBinary.ReadUInt32(header, 12);
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
        {
            throw new PakFormatException($"WZL 图片 {index} 尺寸 {width}x{height} 无效。");
        }

        int rawSize;
        try
        {
            rawSize = PakImageCodec.CalculateRawSize(imageType, flags, width, height);
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException)
        {
            throw new PakFormatException($"WZL 图片 {index} 的像素格式或尺寸无效。", exception);
        }

        var compressedSize = checked((int)compressedSizeValue);
        var payloadSize = compressedSize == 0 ? rawSize : compressedSize;
        var payloadOffset = checked((long)offset + GeePakConstants.ImageHeaderSize);
        PakBinary.EnsureRange(data.Length, payloadOffset, payloadSize);
        if (payloadOffset + payloadSize > nextOffset)
        {
            throw new PakFormatException($"WZL 图片 {index} 的载荷与下一个块重叠。");
        }

        if (compressedSize > 0)
        {
            ValidateZlibHeader(data, checked((int)payloadOffset), index);
        }

        return new PakEntry
        {
            Index = index,
            IsEmpty = false,
            ImageType = imageType,
            Flags = flags,
            Width = width,
            Height = height,
            X = x,
            Y = y,
            RawSize = rawSize,
            CompressedSize = compressedSize,
            Payload = data.AsSpan(checked((int)payloadOffset), payloadSize).ToArray(),
            PlainHeader = header,
            SourceOffset = offset,
            IsModified = false
        };
    }

    /// <summary>
    /// 校验 WZL 压缩载荷的 zlib 头，避免损坏块进入预览解码流程。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="payloadOffset">压缩载荷起始偏移。</param>
    /// <param name="index">图片逻辑索引。</param>
    private static void ValidateZlibHeader(byte[] data, int payloadOffset, int index)
    {
        PakBinary.EnsureRange(data.Length, payloadOffset, 2);
        var cmf = data[payloadOffset];
        var flg = data[payloadOffset + 1];
        if ((cmf & 0x0F) != 8 || ((cmf << 8) + flg) % 31 != 0)
        {
            throw new PakFormatException($"WZL 图片 {index} 的 zlib 头无效。");
        }
    }

    /// <summary>
    /// 创建保留逻辑索引的空槽位。
    /// </summary>
    /// <param name="index">逻辑索引。</param>
    /// <returns>空图片槽位。</returns>
    private static PakEntry CreateEmptyEntry(int index)
    {
        return new PakEntry
        {
            Index = index,
            IsEmpty = true,
            IsModified = false
        };
    }
}
