namespace GeePakEditor.Models;

/// <summary>
/// GEE 图片块使用的像素格式。
/// </summary>
public enum PakImageFormat
{
    /// <summary>8 位固定调色板。</summary>
    Palette8,

    /// <summary>16 位 R5G6B5。</summary>
    R5G6B5,

    /// <summary>24 位 RGB。</summary>
    R8G8B8,

    /// <summary>24 位 RGB 加独立 8 位 Alpha 平面。</summary>
    R8G8B8A8,

    /// <summary>32 位 RGB，Alpha 未使用。</summary>
    X8R8G8B8,

    /// <summary>32 位 ARGB。</summary>
    A8R8G8B8
}
