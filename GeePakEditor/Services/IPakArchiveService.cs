using System.Drawing;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// PAK 归档读取、编辑与保存主链路。
/// </summary>
public interface IPakArchiveService
{
    /// <summary>打开并验证一个 GEEPAK3 文件。</summary>
    PakArchive Open(string filePath, string password);

    /// <summary>解码一个非空图片槽位。</summary>
    Bitmap DecodeImage(PakEntry entry);

    /// <summary>把图片添加为新的逻辑槽位。</summary>
    PakEntry AddImage(PakArchive archive, string imagePath);

    /// <summary>用外部图片替换指定逻辑槽位。</summary>
    void ReplaceImage(PakArchive archive, int index, string imagePath);

    /// <summary>把指定槽位标记为空。</summary>
    void DeleteImage(PakArchive archive, int index);

    /// <summary>把指定槽位导出为 PNG。</summary>
    void ExportImage(PakEntry entry, string outputPath);

    /// <summary>将当前编辑状态保存为 GEEPAK3 文件。</summary>
    void Save(PakArchive archive, string outputPath);
}
