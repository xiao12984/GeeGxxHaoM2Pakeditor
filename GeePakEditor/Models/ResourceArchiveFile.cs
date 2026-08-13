using System.IO;

namespace GeePakEditor.Models;

/// <summary>
/// 资源分类目录中的一个可打开归档文件。
/// </summary>
public sealed class ResourceArchiveFile
{
    /// <summary>
    /// 获取归档文件的完整路径。
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 获取归档文件名，不包含父目录路径。
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// 获取归档文件扩展名，结果保留原始文件名中的大小写。
    /// </summary>
    public string Extension => Path.GetExtension(FilePath);
}
