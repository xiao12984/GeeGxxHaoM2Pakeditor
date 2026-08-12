using System.Buffers.Binary;
using GeePakEditor.Services;

namespace GeePakEditor.Utils;

/// <summary>
/// PAK 小端整数读写与范围校验工具。
/// </summary>
internal static class PakBinary
{
    /// <summary>从指定偏移读取无符号 16 位整数。</summary>
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data.Length, offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));
    }

    /// <summary>从指定偏移读取有符号 16 位整数。</summary>
    public static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data.Length, offset, sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, sizeof(short)));
    }

    /// <summary>从指定偏移读取无符号 32 位整数。</summary>
    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data.Length, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
    }

    /// <summary>向指定偏移写入无符号 16 位整数。</summary>
    public static void WriteUInt16(Span<byte> data, int offset, ushort value)
    {
        EnsureRange(data.Length, offset, sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, sizeof(ushort)), value);
    }

    /// <summary>向指定偏移写入有符号 16 位整数。</summary>
    public static void WriteInt16(Span<byte> data, int offset, short value)
    {
        EnsureRange(data.Length, offset, sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(offset, sizeof(short)), value);
    }

    /// <summary>向指定偏移写入无符号 32 位整数。</summary>
    public static void WriteUInt32(Span<byte> data, int offset, uint value)
    {
        EnsureRange(data.Length, offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, sizeof(uint)), value);
    }

    /// <summary>验证偏移与长度没有越过缓冲区。</summary>
    public static void EnsureRange(int totalLength, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset > totalLength || length > totalLength - offset)
        {
            throw new PakFormatException($"PAK 数据范围越界：offset={offset}, length={length}, total={totalLength}。");
        }
    }
}
