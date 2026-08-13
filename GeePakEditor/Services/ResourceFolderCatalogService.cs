using System.IO;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// 按编辑器约定扫描资源根目录下固定分类的归档文件。
/// </summary>
public sealed class ResourceFolderCatalogService
{
    /// <summary>
    /// 固定分类定义。数组顺序就是界面展示顺序，不能按目录枚举顺序替代。
    /// </summary>
    private static readonly CategoryDefinition[] CategoryDefinitions =
    [
        new("UI", "界面(UI)"),
        new("Items", "游戏道具(Items)"),
        new("NPC", "NPC"),
        new("Mon", "怪物(Mon)"),
        new("Hum", "人物外观(Hum)"),
        new("Objects", "自定义地图砖(Objects)"),
        new("Tiles", "大号地砖(Tiles)"),
        new("SmTiles", "小号地砖(SmTiles)"),
        new("Map", "小地图(Map)"),
        new("Magic", "技能(Magic)"),
        new("MagIcon", "技能图标(MagIcon)"),
        new("Hair", "头部发形(Hair)"),
        new(@"Graphics\Human", @"人物外观(Graphics\Human)"),
        new(@"Graphics\Weapon", @"武器外观(Graphics\Weapon)")
    ];

    /// <summary>
    /// 加载指定资源根目录下的固定分类及其直接归档文件。
    /// </summary>
    /// <param name="rootPath">用户选择的资源根目录。</param>
    /// <returns>按固定分类顺序构建的资源目录清单。</returns>
    /// <exception cref="ArgumentException">根目录参数为空。</exception>
    /// <exception cref="DirectoryNotFoundException">根目录不存在或不可访问。</exception>
    public ResourceFolderCatalog Load(string rootPath)
    {
        var resolvedRootPath = ResolveRootPath(rootPath);
        var categories = new List<ResourceFolderCategory>(CategoryDefinitions.Length);

        // 只按预定义顺序访问分类，确保不同文件系统返回顺序不会影响界面。
        foreach (var definition in CategoryDefinitions)
        {
            var category = TryLoadCategory(resolvedRootPath, definition);
            if (category is not null)
            {
                categories.Add(category);
            }
        }

        return new ResourceFolderCatalog
        {
            RootPath = resolvedRootPath,
            Categories = categories
        };
    }

    /// <summary>
    /// 规范化并验证资源根目录，同时主动探测根目录读取权限。
    /// </summary>
    private static string ResolveRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("资源根目录不能为空。", nameof(rootPath));
        }

        string resolvedRootPath;
        try
        {
            resolvedRootPath = Path.GetFullPath(rootPath);
        }
        catch (ArgumentException exception)
        {
            throw new DirectoryNotFoundException($"资源根目录路径无效：{rootPath}", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new DirectoryNotFoundException($"资源根目录路径无效：{rootPath}", exception);
        }
        catch (PathTooLongException exception)
        {
            throw new DirectoryNotFoundException($"资源根目录路径过长：{rootPath}", exception);
        }

        if (!Directory.Exists(resolvedRootPath))
        {
            throw new DirectoryNotFoundException($"资源根目录不存在或无权限访问：{resolvedRootPath}");
        }

        try
        {
            // MoveNext 才会真正访问目录，能够把根目录权限问题转换为中文异常。
            using var entries = Directory.EnumerateFileSystemEntries(
                resolvedRootPath,
                "*",
                SearchOption.TopDirectoryOnly).GetEnumerator();
            _ = entries.MoveNext();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException($"无权限访问资源根目录：{resolvedRootPath}", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new DirectoryNotFoundException($"资源根目录不存在或无法读取：{resolvedRootPath}", exception);
        }
        catch (IOException exception)
        {
            throw new IOException($"无法读取资源根目录：{resolvedRootPath}", exception);
        }

        return resolvedRootPath;
    }

    /// <summary>
    /// 读取单个分类目录；目录缺失或分类目录无权限时返回空结果。
    /// </summary>
    private static ResourceFolderCategory? TryLoadCategory(
        string rootPath,
        CategoryDefinition definition)
    {
        var directoryPath = Path.Combine(rootPath, definition.RelativePath);
        if (!Directory.Exists(directoryPath))
        {
            // Directory.Exists 对不存在和无权限都返回 false；两者在分类层面都按跳过处理。
            return null;
        }

        try
        {
            var files = Directory.EnumerateFiles(
                    directoryPath,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(IsSupportedArchive)
                .Select(path => new ResourceArchiveFile
                {
                    FilePath = Path.GetFullPath(path)
                })
                .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.FileName, StringComparer.Ordinal)
                .ToArray();

            return new ResourceFolderCategory
            {
                RelativePath = definition.RelativePath,
                DisplayName = definition.DisplayName,
                DirectoryPath = Path.GetFullPath(directoryPath),
                Files = files
            };
        }
        catch (UnauthorizedAccessException)
        {
            // 单个分类不可读时不影响其它分类，按需求静默省略该分类。
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            // 扫描过程中目录被删除时与“目录不存在”保持一致，直接省略分类。
            return null;
        }
        catch (IOException)
        {
            // Windows 文件系统瞬时读错误不应阻断其它分类加载，省略当前分类即可。
            return null;
        }
    }

    /// <summary>
    /// 判断文件扩展名是否为编辑器支持的归档格式。
    /// </summary>
    private static bool IsSupportedArchive(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".pak", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".wzl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 保存相对目录和中文名称的内部定义。
    /// </summary>
    private sealed record CategoryDefinition(string RelativePath, string DisplayName);
}
