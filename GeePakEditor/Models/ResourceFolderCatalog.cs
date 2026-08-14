namespace GeePakEditor.Models;

/// <summary>
/// 用户选择的资源根目录及其中已发现的资源分类。
/// </summary>
public sealed class ResourceFolderCatalog
{
    /// <summary>
    /// 获取资源根目录的完整路径。
    /// </summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// 获取按预定义顺序排列的资源分类集合，未分类补丁位于末尾。
    /// </summary>
    public required IReadOnlyList<ResourceFolderCategory> Categories { get; init; }
}
