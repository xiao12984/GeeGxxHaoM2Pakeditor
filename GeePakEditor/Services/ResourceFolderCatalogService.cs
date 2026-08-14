using System.IO;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// 扫描传奇客户端通用 Data、补丁 Data 以及 Graphics 分类目录中的资源归档。
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
    /// 未能根据文件名可靠判断用途的补丁统一保留在末尾分类，避免自定义补丁被隐藏。
    /// </summary>
    private static readonly CategoryDefinition UnclassifiedCategoryDefinition =
        new("Unclassified", "其他补丁");

    /// <summary>
    /// 平铺 Data 归档的文件名前缀规则。匹配顺序用于保护相近名称，不能按界面顺序替代。
    /// </summary>
    private static readonly FileNameCategoryRule[] FileNameCategoryRules =
    [
        new("MagIcon", ["magicon", "技能图标"]),
        new(@"Graphics\Human", ["humeffect", "cbohum", "huofu", "l-humhorse", "人物特效"]),
        new(@"Graphics\Weapon", ["weaponeffect", "weapon", "武器"]),
        new("SmTiles", ["smtiles"]),
        new("UI", ["newopui", "newui", "chrsel", "prguse", "nselect", "jiemian", "ui", "界面", "按钮"]),
        new("Items", ["dnitems", "stateitem", "items", "道具", "物品"]),
        new("NPC", ["npc"]),
        new("Mon", ["mon", "怪物"]),
        new("Hum", ["hum", "人物"]),
        new("Objects", ["objects"]),
        new("Tiles", ["tiles"]),
        new("Map", ["mmap", "minimap", "map"]),
        new("Magic", ["magic", "mageffect", "cboeffect", "stateeffect", "mag", "jineng", "jn", "技能"]),
        new("Hair", ["l-hairhorse", "cbohair", "hair", "发型"])
    ];

    /// <summary>
    /// 加载所选目录中的平铺归档、固定分类目录及同级 Graphics 数字补丁。
    /// </summary>
    /// <param name="rootPath">用户选择的 Data 目录、补丁根目录或客户端根目录。</param>
    /// <returns>按固定分类顺序构建的资源目录清单。</returns>
    /// <exception cref="ArgumentException">根目录参数为空。</exception>
    /// <exception cref="DirectoryNotFoundException">根目录不存在或不可访问。</exception>
    public ResourceFolderCatalog Load(string rootPath)
    {
        var resolvedRootPath = ResolveRootPath(rootPath);
        var archiveBuckets = CreateArchiveBuckets();
        var visibleCategoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dataRootPaths = GetDataRootPaths(resolvedRootPath);

        foreach (var dataRootPath in dataRootPaths)
        {
            // 通用 Data 与补丁 Data 都可能混放 PAK/WZL，只根据文件名判断分类。
            CollectFlatArchives(
                dataRootPath,
                archiveBuckets,
                visibleCategoryPaths,
                sourceDirectories);

            foreach (var definition in CategoryDefinitions)
            {
                CollectCategoryDirectory(
                    dataRootPath,
                    definition,
                    archiveBuckets,
                    visibleCategoryPaths,
                    sourceDirectories);
            }

            // 用户直接选择 Data 时，Graphics 通常位于其父目录且与 Data 同级。
            CollectSiblingGraphicsDirectories(
                dataRootPath,
                archiveBuckets,
                visibleCategoryPaths,
                sourceDirectories);
        }

        var categories = new List<ResourceFolderCategory>(CategoryDefinitions.Length + 1);
        foreach (var definition in CategoryDefinitions)
        {
            AddCategoryIfVisible(
                definition,
                archiveBuckets,
                visibleCategoryPaths,
                sourceDirectories,
                categories);
        }

        AddCategoryIfVisible(
            UnclassifiedCategoryDefinition,
            archiveBuckets,
            visibleCategoryPaths,
            sourceDirectories,
            categories);

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
    /// 返回需要扫描的资源根目录，包括用户所选目录及其直属 Data 目录。
    /// </summary>
    private static IReadOnlyList<string> GetDataRootPaths(string rootPath)
    {
        var paths = new List<string> { rootPath };
        var childDataPath = Path.Combine(rootPath, "Data");
        if (Directory.Exists(childDataPath))
        {
            var resolvedChildDataPath = Path.GetFullPath(childDataPath);
            if (!paths.Contains(resolvedChildDataPath, StringComparer.OrdinalIgnoreCase))
            {
                // 选择补丁根目录或客户端根目录时，自动纳入其直属 Data/data。
                paths.Add(resolvedChildDataPath);
            }
        }

        return paths;
    }

    /// <summary>
    /// 建立所有固定分类及兜底分类的归档去重容器。
    /// </summary>
    private static Dictionary<string, Dictionary<string, ResourceArchiveFile>> CreateArchiveBuckets()
    {
        var buckets = new Dictionary<string, Dictionary<string, ResourceArchiveFile>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var definition in CategoryDefinitions.Append(UnclassifiedCategoryDefinition))
        {
            buckets.Add(
                definition.RelativePath,
                new Dictionary<string, ResourceArchiveFile>(StringComparer.OrdinalIgnoreCase));
        }

        return buckets;
    }

    /// <summary>
    /// 扫描 Data 根层的 PAK/WZL，并按归档文件名归入固定分类。
    /// </summary>
    private static void CollectFlatArchives(
        string directoryPath,
        IReadOnlyDictionary<string, Dictionary<string, ResourceArchiveFile>> archiveBuckets,
        ISet<string> visibleCategoryPaths,
        IDictionary<string, string> sourceDirectories)
    {
        var archivePaths = TryEnumerateSupportedArchives(directoryPath);
        if (archivePaths is null)
        {
            return;
        }

        foreach (var archivePath in archivePaths)
        {
            var categoryPath = ResolveFileNameCategory(archivePath);
            AddArchive(
                categoryPath,
                archivePath,
                directoryPath,
                archiveBuckets,
                visibleCategoryPaths,
                sourceDirectories);
        }
    }

    /// <summary>
    /// 扫描固定分类目录；Graphics 下数字命名的补丁由其 Human/Weapon 目录直接定类。
    /// </summary>
    private static void CollectCategoryDirectory(
        string rootPath,
        CategoryDefinition definition,
        IReadOnlyDictionary<string, Dictionary<string, ResourceArchiveFile>> archiveBuckets,
        ISet<string> visibleCategoryPaths,
        IDictionary<string, string> sourceDirectories)
    {
        var directoryPath = Path.Combine(rootPath, definition.RelativePath);
        var archivePaths = TryEnumerateSupportedArchives(directoryPath);
        if (archivePaths is null)
        {
            return;
        }

        // 可读分类目录即使暂时为空也继续显示，保持原有固定目录导航行为。
        visibleCategoryPaths.Add(definition.RelativePath);
        sourceDirectories.TryAdd(definition.RelativePath, Path.GetFullPath(directoryPath));
        foreach (var archivePath in archivePaths)
        {
            AddArchive(
                definition.RelativePath,
                archivePath,
                directoryPath,
                archiveBuckets,
                visibleCategoryPaths,
                sourceDirectories);
        }
    }

    /// <summary>
    /// 当扫描目录名为 Data 时，补充读取与其同级的 Graphics/Human 和 Graphics/Weapon。
    /// </summary>
    private static void CollectSiblingGraphicsDirectories(
        string dataRootPath,
        IReadOnlyDictionary<string, Dictionary<string, ResourceArchiveFile>> archiveBuckets,
        ISet<string> visibleCategoryPaths,
        IDictionary<string, string> sourceDirectories)
    {
        if (!string.Equals(
                Path.GetFileName(dataRootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
                "Data",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var parentPath = Directory.GetParent(dataRootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return;
        }

        foreach (var definition in CategoryDefinitions.Where(IsGraphicsCategory))
        {
            CollectCategoryDirectory(
                parentPath,
                definition,
                archiveBuckets,
                visibleCategoryPaths,
                sourceDirectories);
        }
    }

    /// <summary>
    /// 把归档加入指定分类，并按完整路径去重，允许同名 PAK/WZL 同时显示。
    /// </summary>
    private static void AddArchive(
        string categoryPath,
        string archivePath,
        string sourceDirectory,
        IReadOnlyDictionary<string, Dictionary<string, ResourceArchiveFile>> archiveBuckets,
        ISet<string> visibleCategoryPaths,
        IDictionary<string, string> sourceDirectories)
    {
        var resolvedArchivePath = Path.GetFullPath(archivePath);
        archiveBuckets[categoryPath].TryAdd(
            resolvedArchivePath,
            new ResourceArchiveFile { FilePath = resolvedArchivePath });
        visibleCategoryPaths.Add(categoryPath);
        sourceDirectories.TryAdd(categoryPath, Path.GetFullPath(sourceDirectory));
    }

    /// <summary>
    /// 按文件名匹配最具体的分类规则，无法判断的自定义名称进入其他补丁。
    /// </summary>
    private static string ResolveFileNameCategory(string archivePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(archivePath).Trim();
        foreach (var rule in FileNameCategoryRules)
        {
            if (rule.Prefixes.Any(prefix => fileName.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return rule.RelativePath;
            }
        }

        return UnclassifiedCategoryDefinition.RelativePath;
    }

    /// <summary>
    /// 构建可见分类模型，并保持文件名排序稳定。
    /// </summary>
    private static void AddCategoryIfVisible(
        CategoryDefinition definition,
        IReadOnlyDictionary<string, Dictionary<string, ResourceArchiveFile>> archiveBuckets,
        ISet<string> visibleCategoryPaths,
        IReadOnlyDictionary<string, string> sourceDirectories,
        ICollection<ResourceFolderCategory> categories)
    {
        if (!visibleCategoryPaths.Contains(definition.RelativePath))
        {
            return;
        }

        var files = archiveBuckets[definition.RelativePath]
            .Values
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.FileName, StringComparer.Ordinal)
            .ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        categories.Add(new ResourceFolderCategory
        {
            RelativePath = definition.RelativePath,
            DisplayName = definition.DisplayName,
            DirectoryPath = sourceDirectories[definition.RelativePath],
            Files = files
        });
    }

    /// <summary>
    /// 枚举目录第一层中编辑器支持的归档；分类目录缺失或不可读时返回空结果。
    /// </summary>
    private static IReadOnlyList<string>? TryEnumerateSupportedArchives(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(
                    directoryPath,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(IsSupportedArchive)
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            // 单个扩展目录不可读时不影响其它来源，按分类目录无权限处理。
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            // 扫描过程中目录被删除时与“目录不存在”保持一致，直接跳过。
            return null;
        }
        catch (IOException)
        {
            // Windows 文件系统瞬时读错误不应阻断其它分类加载。
            return null;
        }
    }

    /// <summary>
    /// 判断分类是否属于按所在目录定类的 Graphics 数字补丁。
    /// </summary>
    private static bool IsGraphicsCategory(CategoryDefinition definition)
    {
        return definition.RelativePath.StartsWith(
            "Graphics",
            StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// 保存平铺归档的分类路径及其文件名前缀。
    /// </summary>
    private sealed record FileNameCategoryRule(string RelativePath, string[] Prefixes);
}
