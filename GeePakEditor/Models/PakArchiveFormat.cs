namespace GeePakEditor.Models;

/// <summary>
/// 当前已打开资源归档的实际文件格式。
/// </summary>
public enum PakArchiveFormat
{
    /// <summary>可完整读取并写回的 GEEPAK3 归档。</summary>
    GeePak3,

    /// <summary>传统 WZL 数据文件与同名 WZX 索引文件组成的可编辑归档。</summary>
    Wzl
}
