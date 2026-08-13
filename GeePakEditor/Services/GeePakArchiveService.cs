using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using GeePakEditor.Config;
using GeePakEditor.Models;
using GeePakEditor.Utils;

namespace GeePakEditor.Services;

/// <summary>
/// GEEPAK3 归档的精确读取、编辑与重新写入实现。
/// </summary>
public sealed class GeePakArchiveService : IPakArchiveService
{
    private readonly IPakKeyProvider _keyProvider;
    private readonly PakImageCodec _imageCodec;
    private readonly WzlArchiveReader _wzlArchiveReader = new();

    /// <summary>
    /// 注入密钥来源与图片编解码主链路。
    /// </summary>
    public GeePakArchiveService(IPakKeyProvider keyProvider, PakImageCodec imageCodec)
    {
        _keyProvider = keyProvider;
        _imageCodec = imageCodec;
    }

    /// <inheritdoc />
    public bool IsGeePak3Archive(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < GeePakConstants.Signature.Length)
        {
            return false;
        }

        var signature = new byte[GeePakConstants.Signature.Length];
        var readLength = stream.Read(signature, 0, signature.Length);
        return readLength == signature.Length &&
               signature.AsSpan().SequenceEqual(GeePakConstants.Signature);
    }

    /// <inheritdoc />
    public void ValidateArchiveFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[GeePakConstants.HeaderSize];
        var readLength = 0;
        while (readLength < header.Length)
        {
            var count = stream.Read(header, readLength, header.Length - readLength);
            if (count == 0)
            {
                break;
            }

            readLength += count;
        }

        if (readLength != header.Length)
        {
            throw new PakFormatException("文件长度小于 GEEPAK3 固定头长度。");
        }

        ValidateSignature(header);
    }

    /// <inheritdoc />
    public PakArchive Open(string filePath, string password)
    {
        if (!_keyProvider.TryGetProfile(password, out var keyProfile) || keyProfile is null)
        {
            throw new PakFormatException(
                $"密码“{password}”未能生成可用的 GEEPAK3 派生密钥，请确认密码是否正确。");
        }

        var data = File.ReadAllBytes(filePath);
        ValidateSignature(data);
        var plainGlobalHeader = DecryptGlobalHeader(data, keyProfile);
        var fields = ReadGlobalFields(plainGlobalHeader, data.Length);
        var slots = ReadSlots(data, fields.SlotCount, fields.IndexOffset, keyProfile);

        return new PakArchive
        {
            FilePath = Path.GetFullPath(filePath),
            Title = fields.Title,
            Format = PakArchiveFormat.GeePak3,
            CanWrite = true,
            Password = password,
            KeyProfile = keyProfile,
            ReservedBytes = data.AsSpan(8, 2).ToArray(),
            PlainGlobalHeader = plainGlobalHeader,
            Slots = slots
        };
    }

    /// <inheritdoc />
    public PakArchive OpenWzl(string filePath) => _wzlArchiveReader.Open(filePath);

    /// <inheritdoc />
    public PakArchive CreatePak(string filePath, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("新建 PAK 必须提供有效密码。");
        }

        if (!_keyProvider.TryGetProfile(password, out var keyProfile) || keyProfile is null)
        {
            throw new PakFormatException($"密码“{password}”未能生成可用的 GEEPAK3 派生密钥，请确认本机派生组件是否可用。");
        }

        var resolvedPath = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetExtension(resolvedPath), ".pak", StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath = Path.ChangeExtension(resolvedPath, ".pak");
        }

        var title = "www.gameofmir.com";
        var plainHeader = CreateGlobalHeader(title);
        var archive = new PakArchive
        {
            FilePath = resolvedPath,
            Title = title,
            Format = PakArchiveFormat.GeePak3,
            CanWrite = true,
            Password = password,
            KeyProfile = keyProfile,
            ReservedBytes = [0, 0],
            PlainGlobalHeader = plainHeader,
            Slots = []
        };

        // 新建流程立即落盘，确保窗口绑定的归档路径和磁盘文件一致。
        Save(archive, resolvedPath);
        return archive;
    }

    /// <inheritdoc />
    public PakArchive CreateWzl(string filePath)
    {
        var resolvedPath = Path.GetFullPath(filePath);
        var archive = new PakArchive
        {
            FilePath = resolvedPath,
            Title = "WZL/WZX 可编辑资源",
            Format = PakArchiveFormat.Wzl,
            CanWrite = true,
            WzlHeader = new byte[GeePakConstants.WzlHeaderSize],
            WzxHeader = new byte[GeePakConstants.WzxHeaderSize],
            Slots = []
        };

        // WZL 由数据文件和同名 WZX 索引组成，创建时同步生成两个空文件。
        Save(archive, resolvedPath);
        return archive;
    }

    /// <inheritdoc />
    public Bitmap DecodeImage(PakEntry entry) => _imageCodec.Decode(entry);

    /// <inheritdoc />
    public PakEntry AddImage(PakArchive archive, string imagePath)
    {
        EnsureWritableArchive(archive);
        var targetIndex = archive.Slots.FindIndex(entry => entry.IsEmpty);
        if (targetIndex < 0)
        {
            targetIndex = archive.Slots.Count;
            archive.Slots.Add(CreateEmptyEntry(targetIndex));
        }

        var encoded = _imageCodec.EncodeFile(imagePath, targetIndex);
        archive.Slots[targetIndex] = encoded;
        return encoded;
    }

    /// <inheritdoc />
    public void ReplaceImage(PakArchive archive, int index, string imagePath)
    {
        EnsureWritableArchive(archive);
        var current = GetSlot(archive, index);
        archive.Slots[index] = _imageCodec.EncodeFile(imagePath, index, current.X, current.Y);
    }

    /// <inheritdoc />
    public void DeleteImage(PakArchive archive, int index)
    {
        EnsureWritableArchive(archive);
        _ = GetSlot(archive, index);
        archive.Slots[index] = CreateEmptyEntry(index, true);
    }

    /// <inheritdoc />
    public void ExportImage(PakEntry entry, string outputPath)
    {
        using var image = DecodeImage(entry);
        image.Save(outputPath, ImageFormat.Png);
    }

    /// <inheritdoc />
    public void Save(PakArchive archive, string outputPath)
    {
        EnsureWritableArchive(archive);
        if (archive.Slots.Count > GeePakConstants.MaximumSlotCount)
        {
            throw new InvalidOperationException($"槽位数量超过上限 {GeePakConstants.MaximumSlotCount:N0}。");
        }

        switch (archive.Format)
        {
            case PakArchiveFormat.GeePak3:
                SaveGeePak3(archive, outputPath);
                break;
            case PakArchiveFormat.Wzl:
                SaveWzl(archive, outputPath);
                break;
            default:
                throw new InvalidOperationException($"不支持保存的归档格式：{archive.Format}。");
        }
    }

    /// <summary>
    /// 使用现有 GEEPAK3 加密索引链路保存 PAK。
    /// </summary>
    /// <param name="archive">当前内存归档。</param>
    /// <param name="outputPath">目标 PAK 路径。</param>
    private void SaveGeePak3(PakArchive archive, string outputPath)
    {
        var keyProfile = RequireKeyProfile(archive);
        var resolvedOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(resolvedOutputPath)
            ?? throw new InvalidOperationException("保存路径没有有效目录。");
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(resolvedOutputPath)}.{Guid.NewGuid():N}.tmp");
        var newOffsets = new uint[archive.Slots.Count];

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                WriteFileHeader(writer, archive, keyProfile);
                var indexStart = stream.Position;
                writer.Write(new byte[checked(archive.Slots.Count * sizeof(uint))]);

                foreach (var entry in archive.Slots)
                {
                    if (entry.IsEmpty)
                    {
                        continue;
                    }

                    ValidateEntryForSave(entry);
                    if (stream.Position > uint.MaxValue)
                    {
                        throw new InvalidOperationException("PAK 文件超过 GEEPAK3 的 4 GiB 偏移上限。");
                    }

                    newOffsets[entry.Index] = (uint)stream.Position;
                    writer.Write(EncryptImageHeader(entry, keyProfile));
                    writer.Write(entry.Payload);
                }

                stream.Position = indexStart;
                for (var index = 0; index < newOffsets.Length; index++)
                {
                    var key = PakBinary.ReadUInt32(keyProfile.IndexKey, index % 64 * sizeof(uint));
                    var encrypted = newOffsets[index] ^ ~key ^ (uint)index;
                    writer.Write(encrypted);
                }

                writer.Flush();
                stream.Flush(true);
            }

            File.Move(temporaryPath, resolvedOutputPath, true);
            archive.FilePath = resolvedOutputPath;
            for (var index = 0; index < archive.Slots.Count; index++)
            {
                archive.Slots[index].Index = index;
                archive.Slots[index].SourceOffset = newOffsets[index];
                archive.Slots[index].IsModified = false;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// 保存传统 WZL 数据文件，并同步重建同名 WZX 偏移索引。
    /// </summary>
    /// <param name="archive">当前内存归档。</param>
    /// <param name="outputPath">目标 WZL 路径。</param>
    private void SaveWzl(PakArchive archive, string outputPath)
    {
        var resolvedWzlPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(resolvedWzlPath), ".wzl", StringComparison.OrdinalIgnoreCase))
        {
            resolvedWzlPath = Path.ChangeExtension(resolvedWzlPath, ".wzl");
        }

        var resolvedWzxPath = ResolveWzxOutputPath(resolvedWzlPath);
        var outputDirectory = Path.GetDirectoryName(resolvedWzlPath)
            ?? throw new InvalidOperationException("保存路径没有有效目录。");
        Directory.CreateDirectory(outputDirectory);

        var temporaryWzlPath = Path.Combine(outputDirectory, $".{Path.GetFileName(resolvedWzlPath)}.{Guid.NewGuid():N}.tmp");
        var temporaryWzxPath = Path.Combine(outputDirectory, $".{Path.GetFileName(resolvedWzxPath)}.{Guid.NewGuid():N}.tmp");
        var newOffsets = new uint[archive.Slots.Count];

        try
        {
            using (var stream = new FileStream(temporaryWzlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(GetSizedHeader(archive.WzlHeader, GeePakConstants.WzlHeaderSize));

                foreach (var entry in archive.Slots)
                {
                    if (entry.IsEmpty)
                    {
                        continue;
                    }

                    ValidateEntryForSave(entry);
                    if (stream.Position > uint.MaxValue)
                    {
                        throw new InvalidOperationException("WZL 文件超过 4 GiB 偏移上限。");
                    }

                    newOffsets[entry.Index] = (uint)stream.Position;
                    writer.Write(BuildPlainImageHeader(entry));
                    writer.Write(entry.Payload);
                }

                writer.Flush();
                stream.Flush(true);
            }

            WriteWzxIndex(temporaryWzxPath, archive.WzxHeader, newOffsets);
            File.Move(temporaryWzlPath, resolvedWzlPath, true);
            File.Move(temporaryWzxPath, resolvedWzxPath, true);

            archive.FilePath = resolvedWzlPath;
            for (var index = 0; index < archive.Slots.Count; index++)
            {
                archive.Slots[index].Index = index;
                archive.Slots[index].SourceOffset = newOffsets[index];
                archive.Slots[index].IsModified = false;
            }
        }
        finally
        {
            if (File.Exists(temporaryWzlPath))
            {
                File.Delete(temporaryWzlPath);
            }

            if (File.Exists(temporaryWzxPath))
            {
                File.Delete(temporaryWzxPath);
            }
        }
    }

    /// <summary>
    /// 验证文件签名并排除目前没有写回规则的 GEEPAK2。
    /// </summary>
    private static void ValidateSignature(byte[] data)
    {
        if (data.Length < GeePakConstants.HeaderSize)
        {
            throw new PakFormatException("文件长度小于 GEEPAK3 固定头长度。");
        }

        if (!data.AsSpan(0, GeePakConstants.Signature.Length).SequenceEqual(GeePakConstants.Signature))
        {
            throw new PakFormatException("文件签名不是 GEEPAK3；当前 C# 版本仅支持可精确写回的 GEEPAK3。");
        }
    }

    /// <summary>
    /// 使用密码对应密钥还原 256 字节全局头。
    /// </summary>
    private static byte[] DecryptGlobalHeader(byte[] data, PakKeyProfile profile)
    {
        var plain = new byte[GeePakConstants.GlobalHeaderSize];
        for (var index = 0; index < plain.Length; index++)
        {
            plain[index] = (byte)(data[10 + index] ^ profile.GlobalHeaderKey[index]);
        }

        return plain;
    }

    /// <summary>
    /// 创建新 PAK 使用的 256 字节明文全局头，并写入已确认的标题和索引字段。
    /// </summary>
    /// <param name="title">GEEPAK3 全局头中允许的资源标题。</param>
    /// <returns>可交由保存链路加密写入的全局头。</returns>
    private static byte[] CreateGlobalHeader(string title)
    {
        var titleBytes = Encoding.ASCII.GetBytes(title);
        if (titleBytes.Length is 0 or > 40)
        {
            throw new InvalidOperationException("新建 PAK 的全局头标题长度无效。");
        }

        var plain = new byte[GeePakConstants.GlobalHeaderSize];
        plain[1] = checked((byte)titleBytes.Length);
        titleBytes.CopyTo(plain, 2);
        PakBinary.WriteUInt32(plain, 0x2A, GeePakConstants.HeaderSize);
        PakBinary.WriteUInt32(plain, 0x2E, 0);
        PakBinary.WriteUInt32(plain, 0x32, 2);
        PakBinary.WriteUInt32(plain, 0x36, GeePakConstants.HeaderSize);
        return plain;
    }

    /// <summary>
    /// 读取并验证全局头中已确认的标题、版本与索引范围。
    /// </summary>
    private static GlobalFields ReadGlobalFields(byte[] plainHeader, int fileLength)
    {
        var titleLength = plainHeader[1];
        if (titleLength == 0 || titleLength > 40 || 2 + titleLength > plainHeader.Length)
        {
            throw new PakFormatException("全局头标题长度无效，密码或密钥可能不正确。");
        }

        var title = Encoding.ASCII.GetString(plainHeader, 2, titleLength);
        var headerSize = PakBinary.ReadUInt32(plainHeader, 0x2A);
        var slotCountValue = PakBinary.ReadUInt32(plainHeader, 0x2E);
        var version = PakBinary.ReadUInt32(plainHeader, 0x32);
        var indexOffsetValue = PakBinary.ReadUInt32(plainHeader, 0x36);
        if (title is not ("www.gameofmir.com" or "www.gameofmir2.com") ||
            headerSize != GeePakConstants.HeaderSize ||
            version != 2 ||
            indexOffsetValue != GeePakConstants.HeaderSize ||
            slotCountValue > GeePakConstants.MaximumSlotCount)
        {
            throw new PakFormatException("GEEPAK3 全局头校验失败，密码或派生密钥不正确。");
        }

        var slotCount = checked((int)slotCountValue);
        var indexOffset = checked((int)indexOffsetValue);
        PakBinary.EnsureRange(fileLength, indexOffset, checked(slotCount * sizeof(uint)));
        return new GlobalFields(title, slotCount, indexOffset);
    }

    /// <summary>
    /// 解密索引、块头和载荷，并验证块之间没有重叠。
    /// </summary>
    private List<PakEntry> ReadSlots(byte[] data, int slotCount, int indexOffset, PakKeyProfile profile)
    {
        var offsets = new uint[slotCount];
        var occupied = new List<(int Index, uint Offset)>();
        var seenOffsets = new HashSet<uint>();
        var indexEnd = checked(indexOffset + slotCount * sizeof(uint));
        for (var index = 0; index < slotCount; index++)
        {
            var encrypted = PakBinary.ReadUInt32(data, indexOffset + index * sizeof(uint));
            var key = PakBinary.ReadUInt32(profile.IndexKey, index % 64 * sizeof(uint));
            var offset = encrypted ^ ~key ^ (uint)index;
            offsets[index] = offset;
            if (offset == 0)
            {
                continue;
            }

            if (offset < indexEnd || offset > data.Length - GeePakConstants.ImageHeaderSize)
            {
                throw new PakFormatException($"图片 {index} 的块头偏移 {offset} 越界。");
            }

            if (!seenOffsets.Add(offset))
            {
                throw new PakFormatException($"图片 {index} 与其他槽位使用了重复块偏移 {offset}。");
            }

            occupied.Add((index, offset));
        }

        occupied.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        var slots = Enumerable.Range(0, slotCount).Select(index => CreateEmptyEntry(index)).ToList();
        for (var order = 0; order < occupied.Count; order++)
        {
            var item = occupied[order];
            var nextOffset = order + 1 < occupied.Count ? occupied[order + 1].Offset : (uint)data.Length;
            slots[item.Index] = ReadEntry(data, item.Index, item.Offset, nextOffset, profile);
        }

        return slots;
    }

    /// <summary>
    /// 解密单个 16 字节块头并复制其载荷。
    /// </summary>
    private PakEntry ReadEntry(byte[] data, int index, uint offset, uint nextOffset, PakKeyProfile profile)
    {
        var header = new byte[GeePakConstants.ImageHeaderSize];
        var keyOffset = index % 64 * GeePakConstants.ImageHeaderSize;
        for (var byteIndex = 0; byteIndex < header.Length; byteIndex++)
        {
            header[byteIndex] = (byte)(data[(int)offset + byteIndex] ^ profile.ImageHeaderKey[keyOffset + byteIndex]);
        }

        var imageType = header[0];
        var flags = header[3];
        var width = PakBinary.ReadUInt16(header, 4);
        var height = PakBinary.ReadUInt16(header, 6);
        var x = PakBinary.ReadInt16(header, 8);
        var y = PakBinary.ReadInt16(header, 10);
        var compressedSizeValue = PakBinary.ReadUInt32(header, 12);
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
        {
            throw new PakFormatException($"图片 {index} 尺寸 {width}x{height} 无效，密码或块头密钥可能不正确。");
        }

        int rawSize;
        try
        {
            rawSize = PakImageCodec.CalculateRawSize(imageType, flags, width, height);
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException)
        {
            throw new PakFormatException($"图片 {index} 的像素格式或尺寸无效。", exception);
        }

        var compressedSize = checked((int)compressedSizeValue);
        var payloadSize = compressedSize == 0 ? rawSize : compressedSize;
        var payloadOffset = checked((long)offset + GeePakConstants.ImageHeaderSize);
        PakBinary.EnsureRange(data.Length, payloadOffset, payloadSize);
        if (payloadOffset + payloadSize > nextOffset)
        {
            throw new PakFormatException($"图片 {index} 的载荷与下一个块重叠。");
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
    /// 校验 zlib 头的压缩方法和 FCHECK。
    /// </summary>
    private static void ValidateZlibHeader(byte[] data, int payloadOffset, int index)
    {
        PakBinary.EnsureRange(data.Length, payloadOffset, 2);
        var cmf = data[payloadOffset];
        var flg = data[payloadOffset + 1];
        if ((cmf & 0x0F) != 8 || ((cmf << 8) + flg) % 31 != 0)
        {
            throw new PakFormatException($"图片 {index} 的 zlib 头无效。");
        }
    }

    /// <summary>
    /// 写入签名、保留字段和重新加密后的全局头。
    /// </summary>
    private static void WriteFileHeader(BinaryWriter writer, PakArchive archive, PakKeyProfile keyProfile)
    {
        writer.Write(GeePakConstants.Signature);
        writer.Write(archive.ReservedBytes.Length == 2 ? archive.ReservedBytes : new byte[] { 0, 0 });

        var plain = archive.PlainGlobalHeader.ToArray();
        if (plain.Length != GeePakConstants.GlobalHeaderSize)
        {
            throw new InvalidOperationException("归档中的全局头长度无效。");
        }

        PakBinary.WriteUInt32(plain, 0x2A, GeePakConstants.HeaderSize);
        PakBinary.WriteUInt32(plain, 0x2E, checked((uint)archive.Slots.Count));
        PakBinary.WriteUInt32(plain, 0x32, 2);
        PakBinary.WriteUInt32(plain, 0x36, GeePakConstants.HeaderSize);
        var encrypted = new byte[plain.Length];
        for (var index = 0; index < encrypted.Length; index++)
        {
            encrypted[index] = (byte)(plain[index] ^ keyProfile.GlobalHeaderKey[index]);
        }

        writer.Write(encrypted);
    }

    /// <summary>
    /// 按 WZL 文件名推导同名 WZX 索引文件路径。
    /// </summary>
    /// <param name="wzlPath">目标 WZL 数据文件路径。</param>
    /// <returns>同目录同名的 WZX 索引文件路径。</returns>
    private static string ResolveWzxOutputPath(string wzlPath)
    {
        return Path.ChangeExtension(wzlPath, ".wzx");
    }

    /// <summary>
    /// 返回固定长度头部；长度不匹配时使用零填充，避免损坏新建文件结构。
    /// </summary>
    /// <param name="source">归档中保留的原始头部。</param>
    /// <param name="length">目标格式要求的头部长度。</param>
    /// <returns>长度固定的头部副本。</returns>
    private static byte[] GetSizedHeader(byte[] source, int length)
    {
        if (source.Length == length)
        {
            return source.ToArray();
        }

        return new byte[length];
    }

    /// <summary>
    /// 写入 WZX 头部和每个逻辑槽位对应的 WZL 数据偏移。
    /// </summary>
    /// <param name="wzxPath">临时 WZX 文件路径。</param>
    /// <param name="header">原始或新建 WZX 头部。</param>
    /// <param name="offsets">按逻辑槽位排列的 WZL 块偏移。</param>
    private static void WriteWzxIndex(string wzxPath, byte[] header, IReadOnlyList<uint> offsets)
    {
        var writableHeader = GetSizedHeader(header, GeePakConstants.WzxHeaderSize);
        PakBinary.WriteUInt32(writableHeader, GeePakConstants.WzxHeaderSize - sizeof(uint), checked((uint)offsets.Count));

        using var stream = new FileStream(wzxPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(writableHeader);
        foreach (var offset in offsets)
        {
            writer.Write(offset);
        }

        writer.Flush();
        stream.Flush(true);
    }

    /// <summary>
    /// 生成一个完整的 16 字节明文图片块头，供 WZL 直接写入或 PAK 继续加密。
    /// </summary>
    /// <param name="entry">需要保存的图片槽位。</param>
    /// <returns>已同步尺寸、偏移和压缩长度的明文块头。</returns>
    private static byte[] BuildPlainImageHeader(PakEntry entry)
    {
        var plain = entry.PlainHeader.Length == GeePakConstants.ImageHeaderSize
            ? entry.PlainHeader.ToArray()
            : new byte[GeePakConstants.ImageHeaderSize];
        plain[0] = entry.ImageType;
        plain[3] = entry.Flags;
        PakBinary.WriteUInt16(plain, 4, checked((ushort)entry.Width));
        PakBinary.WriteUInt16(plain, 6, checked((ushort)entry.Height));
        PakBinary.WriteInt16(plain, 8, entry.X);
        PakBinary.WriteInt16(plain, 10, entry.Y);
        PakBinary.WriteUInt32(plain, 12, checked((uint)entry.CompressedSize));
        return plain;
    }

    /// <summary>
    /// 生成并加密一个完整的 16 字节图片块头。
    /// </summary>
    private static byte[] EncryptImageHeader(PakEntry entry, PakKeyProfile profile)
    {
        var plain = BuildPlainImageHeader(entry);
        var encrypted = new byte[plain.Length];
        var keyOffset = entry.Index % 64 * GeePakConstants.ImageHeaderSize;
        for (var byteIndex = 0; byteIndex < encrypted.Length; byteIndex++)
        {
            encrypted[byteIndex] = (byte)(plain[byteIndex] ^ profile.ImageHeaderKey[keyOffset + byteIndex]);
        }

        return encrypted;
    }

    /// <summary>
    /// 保存前确认索引、尺寸、载荷与压缩标记一致。
    /// </summary>
    private static void ValidateEntryForSave(PakEntry entry)
    {
        if (entry.Index < 0 || entry.Width is < 1 or > 4096 || entry.Height is < 1 or > 4096)
        {
            throw new InvalidOperationException($"图片 {entry.Index} 的元数据无效。");
        }

        var expectedRawSize = PakImageCodec.CalculateRawSize(entry.ImageType, entry.Flags, entry.Width, entry.Height);
        if (expectedRawSize != entry.RawSize)
        {
            throw new InvalidOperationException($"图片 {entry.Index} 的原始长度与像素格式不一致。");
        }

        var expectedPayloadSize = entry.CompressedSize == 0 ? entry.RawSize : entry.CompressedSize;
        if (entry.Payload.Length != expectedPayloadSize)
        {
            throw new InvalidOperationException($"图片 {entry.Index} 的载荷长度无效。");
        }
    }

    /// <summary>
    /// 返回索引存在的槽位，否则报告调用错误。
    /// </summary>
    private static PakEntry GetSlot(PakArchive archive, int index)
    {
        if (index < 0 || index >= archive.Slots.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "图片索引超出当前归档范围。");
        }

        return archive.Slots[index];
    }

    /// <summary>
    /// 确认当前归档允许通过 GEEPAK3 写回链路修改。
    /// </summary>
    /// <param name="archive">待修改或保存的归档。</param>
    private static void EnsureWritableArchive(PakArchive archive)
    {
        if (!archive.CanWrite)
        {
            throw new InvalidOperationException("当前归档格式不允许写回或修改。");
        }
    }

    /// <summary>
    /// 获取 GEEPAK3 写回必须使用的精确密钥。
    /// </summary>
    /// <param name="archive">需要保存的 GEEPAK3 归档。</param>
    /// <returns>归档对应的密钥配置。</returns>
    private static PakKeyProfile RequireKeyProfile(PakArchive archive)
    {
        return archive.KeyProfile ?? throw new InvalidOperationException("当前归档缺少 GEEPAK3 写回密钥。");
    }

    /// <summary>
    /// 创建一个不占用物理块的空逻辑槽位。
    /// </summary>
    private static PakEntry CreateEmptyEntry(int index, bool modified = false)
    {
        return new PakEntry
        {
            Index = index,
            IsEmpty = true,
            IsModified = modified
        };
    }

    /// <summary>
    /// 全局头中已确认且会参与验证的字段。
    /// </summary>
    private sealed record GlobalFields(string Title, int SlotCount, int IndexOffset);
}
