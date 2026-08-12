using System.ComponentModel;

namespace GeePakEditor.Models;

/// <summary>
/// PAK 中一个逻辑图片槽位及其块元数据。
/// </summary>
public sealed class PakEntry
{
    /// <summary>逻辑索引，保存时保持稳定。</summary>
    [DisplayName("索引")]
    [ReadOnly(true)]
    public int Index { get; internal set; }

    /// <summary>当前槽位是否为空。</summary>
    [Browsable(false)]
    public bool IsEmpty { get; internal set; }

    /// <summary>块头中的图片类型字节。</summary>
    [Browsable(false)]
    public byte ImageType { get; internal set; }

    /// <summary>块头中的 Alpha 标志。</summary>
    [Browsable(false)]
    public byte Flags { get; internal set; }

    /// <summary>图片宽度。</summary>
    [DisplayName("宽度")]
    [ReadOnly(true)]
    public int Width { get; internal set; }

    /// <summary>图片高度。</summary>
    [DisplayName("高度")]
    [ReadOnly(true)]
    public int Height { get; internal set; }

    /// <summary>绘制 X 偏移，可在属性面板中修改。</summary>
    [DisplayName("X 偏移")]
    public short X { get; set; }

    /// <summary>绘制 Y 偏移，可在属性面板中修改。</summary>
    [DisplayName("Y 偏移")]
    public short Y { get; set; }

    /// <summary>解压后的像素字节数。</summary>
    [DisplayName("原始大小")]
    [ReadOnly(true)]
    public int RawSize { get; internal set; }

    /// <summary>压缩载荷长度，零表示未压缩。</summary>
    [Browsable(false)]
    public int CompressedSize { get; internal set; }

    /// <summary>块载荷；未修改图片保持原始压缩数据。</summary>
    [Browsable(false)]
    public byte[] Payload { get; internal set; } = [];

    /// <summary>解密后的完整块头，用于保存时保留尚未识别的字段。</summary>
    [Browsable(false)]
    public byte[] PlainHeader { get; internal set; } = new byte[16];

    /// <summary>原文件中的块偏移，仅用于诊断。</summary>
    [DisplayName("块偏移")]
    [ReadOnly(true)]
    public long SourceOffset { get; internal set; }

    /// <summary>该槽位是否已在当前编辑会话中修改。</summary>
    [Browsable(false)]
    public bool IsModified { get; internal set; }

    /// <summary>用于列表显示的槽位状态。</summary>
    [DisplayName("状态")]
    [ReadOnly(true)]
    public string StateText => IsEmpty ? "空" : IsModified ? "已修改" : "正常";

    /// <summary>用于列表显示的像素格式。</summary>
    [DisplayName("格式")]
    [ReadOnly(true)]
    public string FormatText => IsEmpty ? string.Empty : GetFormat().ToString();

    /// <summary>用于列表显示的压缩状态。</summary>
    [DisplayName("压缩")]
    [ReadOnly(true)]
    public string CompressionText => IsEmpty ? string.Empty : CompressedSize > 0 ? $"zlib ({CompressedSize:N0})" : "原始";

    /// <summary>
    /// 根据块头类型与标志返回严格像素格式。
    /// </summary>
    public PakImageFormat GetFormat()
    {
        return (ImageType, Flags) switch
        {
            (3, 0) => PakImageFormat.Palette8,
            (5, 0) => PakImageFormat.R5G6B5,
            (6, 0) => PakImageFormat.R8G8B8,
            (6, 1) => PakImageFormat.R8G8B8A8,
            (7, 0) => PakImageFormat.X8R8G8B8,
            (7, 1) => PakImageFormat.A8R8G8B8,
            _ => throw new InvalidDataException($"不支持的 GEE 图片格式：type={ImageType}, flags={Flags}。")
        };
    }
}
