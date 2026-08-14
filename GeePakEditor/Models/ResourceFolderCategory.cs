namespace GeePakEditor.Models;

/// <summary>
/// 一个资源分类及其可打开归档文件。
/// </summary>
public sealed class ResourceFolderCategory
{
    /// <summary>
    /// 获取分类对应的相对目录或内部分类标识，例如 <c>Graphics\Human</c>。
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// 获取显示在界面上的中文分类名称。
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 获取分类的主要来源目录；合并平铺 Data 与 Graphics 时作为节点来源路径。
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// 获取分类中按名称排序并按完整路径去重的归档文件。
    /// </summary>
    public required IReadOnlyList<ResourceArchiveFile> Files { get; init; }
}
