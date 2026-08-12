using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// 封装缩略图网格当前可见且尚未缓存的资源槽位。
/// </summary>
public sealed class ThumbnailRequestEventArgs : EventArgs
{
    /// <summary>
    /// 获取需要生成缩略图的非空资源槽位。
    /// </summary>
    public required IReadOnlyList<PakEntry> Entries { get; init; }
}
