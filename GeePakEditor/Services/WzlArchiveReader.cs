using System.IO;
using GeePakEditor.Config;
using GeePakEditor.Models;
using GeePakEditor.Utils;

namespace GeePakEditor.Services;

/// <summary>
/// 读取传统 WZL 数据文件及同名 WZX 索引文件的服务。
/// </summary>
internal sealed class WzlArchiveReader
{
    /// <summary>
    /// 打开 WZL/WZX 归档，并转换为现有预览与编辑链路可识别的图片槽位。
    /// </summary>
    /// <param name="filePath">用户选择的 WZL 文件路径。</param>
    /// <returns>已读取的 WZL 归档；原始 M2Zip 归档按参考实现标记为只读。</returns>
    public PakArchive Open(string filePath)
    {
        var resolvedWzlPath = Path.GetFullPath(filePath);
        var resolvedWzxPath = ResolveWzxPath(resolvedWzlPath);
        var data = File.ReadAllBytes(resolvedWzlPath);
        if (data.Length < GeePakConstants.WzlHeaderSize)
        {
            throw new PakFormatException("WZL 文件长度小于固定头长度。");
        }

        var wzxIndex = ReadIndexOffsets(resolvedWzxPath);
        var readMode = DetectReadMode(data, wzxIndex.Offsets);
        var slots = ReadSlots(data, wzxIndex.Offsets, readMode);
        var isM2Zip = readMode == WzlReadMode.M2Zip;

        return new PakArchive
        {
            FilePath = resolvedWzlPath,
            Title = isM2Zip ? "M2Zip WZL/WZX（只读）" : "WZL/WZX 可编辑资源",
            Format = PakArchiveFormat.Wzl,
            CanWrite = !isM2Zip,
            WzlHeader = data.AsSpan(0, GeePakConstants.WzlHeaderSize).ToArray(),
            WzxHeader = wzxIndex.Header,
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
    /// <returns>WZX 原始头部和按逻辑索引排列的 WZL 块偏移数组。</returns>
    private static WzxIndex ReadIndexOffsets(string wzxPath)
    {
        var data = File.ReadAllBytes(wzxPath);
        if (data.Length < GeePakConstants.WzxHeaderSize || (data.Length - GeePakConstants.WzxHeaderSize) % sizeof(uint) != 0)
        {
            throw new PakFormatException("WZX 索引文件长度无效。");
        }

        var headerSlotCount = PakBinary.ReadUInt32(data, GeePakConstants.WzxHeaderSize - sizeof(uint));
        var tableSlotCount = checked((data.Length - GeePakConstants.WzxHeaderSize) / sizeof(uint));
        if (headerSlotCount > checked((uint)tableSlotCount))
        {
            throw new PakFormatException($"WZX 偏移表不完整：头部声明={headerSlotCount}, 表={tableSlotCount}。");
        }

        if (headerSlotCount > GeePakConstants.MaximumSlotCount)
        {
            throw new PakFormatException($"WZX 槽位数量超过上限 {GeePakConstants.MaximumSlotCount:N0}。");
        }

        // xiami 的 M2Zip Reader 以头部 IndexCount 为准读取前 N 个偏移；
        // 部分客户端 WZX 尾部会附带额外 UInt32 表项，不能因此阻断正常打开。
        var slotCount = checked((int)headerSlotCount);
        var offsets = new uint[slotCount];
        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] = PakBinary.ReadUInt32(data, GeePakConstants.WzxHeaderSize + index * sizeof(uint));
        }

        return new WzxIndex(offsets, data.AsSpan(0, GeePakConstants.WzxHeaderSize).ToArray());
    }

    /// <summary>
    /// 根据全部物理图片块的共同结构识别 WZL 的读取变体。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="offsets">WZX 中读取出的逻辑槽位偏移。</param>
    /// <returns>后续读取图片块所需的格式变体。</returns>
    private static WzlReadMode DetectReadMode(byte[] data, IReadOnlyList<uint> offsets)
    {
        var hasPhysicalBlock = false;
        var hasOnlyM2ZipBlocks = true;
        var seenOffsets = new HashSet<uint>();
        foreach (var offset in offsets)
        {
            if (offset == 0 || offset == GeePakConstants.WzxHeaderSize)
            {
                continue;
            }

            ValidateImageOffset(data, offset, -1);
            if (!seenOffsets.Add(offset))
            {
                continue;
            }

            var header = data.AsSpan(checked((int)offset), GeePakConstants.ImageHeaderSize);
            hasPhysicalBlock = true;
            if (!LooksLikeM2ZipHeader(header))
            {
                hasOnlyM2ZipBlocks = false;
            }
        }

        // xiami 将第 1 至第 3 字节定义为未确认保留字段，不能据此判定格式。
        // 只有全部物理块均为 M2Zip 已确认的 Encode 3/5 时，才按 M2Zip 只读解析。
        return hasPhysicalBlock && hasOnlyM2ZipBlocks ? WzlReadMode.M2Zip : WzlReadMode.GeePak;
    }

    /// <summary>
    /// 判断图片块是否符合 xiami M2Zip 的头部布局。
    /// </summary>
    /// <param name="header">图片块前 16 字节头部。</param>
    /// <returns>符合 M2Zip 已确认编码类型时返回 true。</returns>
    private static bool LooksLikeM2ZipHeader(ReadOnlySpan<byte> header)
    {
        var imageType = header[0];
        return imageType is 3 or 5;
    }

    /// <summary>
    /// 按 WZX 偏移表读取 WZL 中的全部非空图片块，并保留空槽位。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="offsets">WZX 中读取出的逻辑槽位偏移。</param>
    /// <param name="readMode">当前 WZL 使用的图片头解释方式。</param>
    /// <returns>按逻辑索引排列的图片槽位。</returns>
    private static List<PakEntry> ReadSlots(
        byte[] data,
        IReadOnlyList<uint> offsets,
        WzlReadMode readMode)
    {
        if (data.Length < GeePakConstants.WzlHeaderSize)
        {
            throw new PakFormatException("WZL 文件长度小于固定头长度。");
        }

        var occupied = new List<(int Index, uint Offset)>();
        var distinctOffsets = new HashSet<uint>();
        for (var index = 0; index < offsets.Count; index++)
        {
            var offset = offsets[index];
            if (offset == 0 || offset == GeePakConstants.WzxHeaderSize)
            {
                continue;
            }

            ValidateImageOffset(data, offset, index);
            occupied.Add((index, offset));
            distinctOffsets.Add(offset);
        }

        var physicalOffsets = distinctOffsets.OrderBy(offset => offset).ToArray();
        var entriesByOffset = new Dictionary<uint, PakEntry>(physicalOffsets.Length);
        for (var order = 0; order < physicalOffsets.Length; order++)
        {
            var offset = physicalOffsets[order];
            var nextOffset = order + 1 < physicalOffsets.Length
                ? physicalOffsets[order + 1]
                : checked((uint)data.Length);
            entriesByOffset[offset] = ReadEntry(data, offset, nextOffset, readMode);
        }

        var slots = Enumerable.Range(0, offsets.Count).Select(CreateEmptyEntry).ToList();
        foreach (var item in occupied)
        {
            slots[item.Index] = CloneEntryForIndex(entriesByOffset[item.Offset], item.Index);
        }

        return slots;
    }

    /// <summary>
    /// 读取单个 WZL 图片块头和载荷，并验证压缩长度与块边界。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="offset">图片块在 WZL 中的起始偏移。</param>
    /// <param name="nextOffset">物理顺序中的下一个图片块偏移。</param>
    /// <param name="readMode">当前 WZL 使用的图片头解释方式。</param>
    /// <returns>可交给图片解码器的槽位。</returns>
    private static PakEntry ReadEntry(byte[] data, uint offset, uint nextOffset, WzlReadMode readMode)
    {
        var header = data.AsSpan(checked((int)offset), GeePakConstants.ImageHeaderSize).ToArray();
        var imageType = header[0];
        var flags = readMode == WzlReadMode.M2Zip ? (byte)0 : header[3];
        var width = PakBinary.ReadUInt16(header, 4);
        var height = PakBinary.ReadUInt16(header, 6);
        var x = PakBinary.ReadInt16(header, 8);
        var y = PakBinary.ReadInt16(header, 10);
        var compressedSizeValue = PakBinary.ReadUInt32(header, 12);
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
        {
            throw new PakFormatException($"WZL 图片块偏移 {offset} 的尺寸 {width}x{height} 无效。");
        }

        if (readMode == WzlReadMode.M2Zip && imageType is not (3 or 5))
        {
            throw new PakFormatException($"WZL 图片块偏移 {offset} 的 M2Zip 编码类型 {imageType} 不受支持。");
        }

        int rawSize;
        try
        {
            rawSize = PakImageCodec.CalculateRawSize(imageType, flags, width, height);
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException)
        {
            throw new PakFormatException($"WZL 图片块偏移 {offset} 的像素格式或尺寸无效。", exception);
        }

        int compressedSize;
        try
        {
            compressedSize = checked((int)compressedSizeValue);
        }
        catch (OverflowException exception)
        {
            throw new PakFormatException($"WZL 图片块偏移 {offset} 的载荷长度过大。", exception);
        }

        var payloadSize = compressedSize == 0 ? rawSize : compressedSize;
        var payloadOffset = checked((long)offset + GeePakConstants.ImageHeaderSize);
        PakBinary.EnsureRange(data.Length, payloadOffset, payloadSize);
        if (payloadOffset + payloadSize > nextOffset)
        {
            throw new PakFormatException($"WZL 图片块偏移 {offset} 的载荷与下一个块重叠。");
        }

        if (compressedSize > 0)
        {
            ValidateZlibHeader(data, checked((int)payloadOffset), offset);
        }

        return new PakEntry
        {
            Index = -1,
            IsEmpty = false,
            ImageType = imageType,
            Flags = flags,
            Width = width,
            Height = height,
            X = x,
            Y = y,
            RawSize = rawSize,
            AllowsRawPayloadTail = readMode == WzlReadMode.M2Zip,
            CompressedSize = compressedSize,
            Payload = data.AsSpan(checked((int)payloadOffset), payloadSize).ToArray(),
            PlainHeader = header,
            SourceOffset = offset,
            IsModified = false
        };
    }

    /// <summary>
    /// 校验 WZX 指向的物理图片块偏移，允许 M2Zip 使用的 48 字节空槽哨兵由调用方跳过。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="offset">待校验的图片块偏移。</param>
    /// <param name="index">逻辑索引；-1 表示格式识别阶段。</param>
    private static void ValidateImageOffset(byte[] data, uint offset, int index)
    {
        if (offset < GeePakConstants.WzlHeaderSize ||
            offset > checked((uint)(data.Length - GeePakConstants.ImageHeaderSize)))
        {
            var imageName = index >= 0 ? $"图片 {index}" : "图片块";
            throw new PakFormatException($"WZL {imageName} 的块头偏移 {offset} 越界。");
        }
    }

    /// <summary>
    /// 校验 WZL 压缩载荷的 zlib 头，避免损坏块进入预览解码流程。
    /// </summary>
    /// <param name="data">完整 WZL 文件数据。</param>
    /// <param name="payloadOffset">压缩载荷起始偏移。</param>
    /// <param name="offset">图片块的物理偏移。</param>
    private static void ValidateZlibHeader(byte[] data, int payloadOffset, uint offset)
    {
        PakBinary.EnsureRange(data.Length, payloadOffset, 2);
        var cmf = data[payloadOffset];
        var flg = data[payloadOffset + 1];
        if ((cmf & 0x0F) != 8 || ((cmf << 8) + flg) % 31 != 0)
        {
            throw new PakFormatException($"WZL 图片块偏移 {offset} 的 zlib 头无效。");
        }
    }

    /// <summary>
    /// 为重复引用同一物理块的逻辑槽位创建独立元数据副本。
    /// </summary>
    /// <param name="source">按物理块解析出的图片槽位。</param>
    /// <param name="index">目标逻辑索引。</param>
    /// <returns>绑定到目标逻辑索引的图片槽位。</returns>
    private static PakEntry CloneEntryForIndex(PakEntry source, int index)
    {
        return new PakEntry
        {
            Index = index,
            IsEmpty = source.IsEmpty,
            ImageType = source.ImageType,
            Flags = source.Flags,
            Width = source.Width,
            Height = source.Height,
            X = source.X,
            Y = source.Y,
            RawSize = source.RawSize,
            AllowsRawPayloadTail = source.AllowsRawPayloadTail,
            CompressedSize = source.CompressedSize,
            Payload = source.Payload.ToArray(),
            PlainHeader = source.PlainHeader.ToArray(),
            SourceOffset = source.SourceOffset,
            IsModified = source.IsModified
        };
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

    /// <summary>
    /// 已读取的 WZX 偏移表和原始头部。
    /// </summary>
    /// <param name="Offsets">按逻辑槽位排列的 WZL 图片块偏移。</param>
    /// <param name="Header">WZX 文件前 48 字节头部。</param>
    private sealed record WzxIndex(uint[] Offsets, byte[] Header);

    /// <summary>
    /// WZL 图片块的头部解释方式。
    /// </summary>
    private enum WzlReadMode
    {
        /// <summary>当前项目创建的 GEE 图片块，允许使用现有 WZL 写回链路。</summary>
        GeePak,

        /// <summary>xiami 参考实现使用的 M2Zip 图片块，只读打开。</summary>
        M2Zip
    }
}
