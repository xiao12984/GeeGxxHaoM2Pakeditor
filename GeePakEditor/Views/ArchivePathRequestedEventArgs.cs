namespace GeePakEditor.Views;

/// <summary>
/// 封装目录树中用户指定的归档文件路径。
/// </summary>
public sealed class ArchivePathRequestedEventArgs : EventArgs
{
    /// <summary>
    /// 获取需要通过既有打开流程加载的 PAK 或 WZL 文件完整路径。
    /// </summary>
    public required string FilePath { get; init; }
}
