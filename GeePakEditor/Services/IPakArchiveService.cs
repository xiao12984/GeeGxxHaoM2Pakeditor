using System.Drawing;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// PAK 归档读取、编辑与保存主链路。
/// </summary>
public interface IPakArchiveService
{
    /// <summary>快速判断文件是否带有 GEEPAK3 明文签名。</summary>
    bool IsGeePak3Archive(string filePath);

    /// <summary>验证文件扩展名已筛选后的归档签名和固定头长度。</summary>
    void ValidateArchiveFile(string filePath);

    /// <summary>打开并验证一个 GEEPAK3 文件。</summary>
    PakArchive Open(string filePath, string password);

    /// <summary>打开传统 WZL 数据文件及同名 WZX 索引文件。</summary>
    PakArchive OpenWzl(string filePath);

    /// <summary>创建并写入一个空的 GEEPAK3 归档。</summary>
    PakArchive CreatePak(string filePath, string password);

    /// <summary>创建并写入一个空的传统 WZL/WZX 归档。</summary>
    PakArchive CreateWzl(string filePath);

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

    /// <summary>将当前编辑状态按归档格式保存到磁盘。</summary>
    void Save(PakArchive archive, string outputPath);
}
