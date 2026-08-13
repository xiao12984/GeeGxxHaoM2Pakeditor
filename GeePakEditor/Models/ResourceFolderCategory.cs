namespace GeePakEditor.Models;

/// <summary>
/// 一个资源分类目录及其可打开归档文件。
/// </summary>
public sealed class ResourceFolderCategory
{
    /// <summary>
    /// 获取分类对应的相对目录，例如 <c>Graphics\Human</c>。
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// 获取显示在界面上的中文分类名称。
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 获取分类目录的完整路径。
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// 获取分类目录第一层中按名称排序的归档文件。
    /// </summary>
    public required IReadOnlyList<ResourceArchiveFile> Files { get; init; }
}
