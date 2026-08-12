namespace GeePakEditor.Models;

/// <summary>
/// 已读取的资源归档及当前编辑状态。
/// </summary>
public sealed class PakArchive
{
    /// <summary>当前来源文件路径。</summary>
    public required string FilePath { get; set; }

    /// <summary>归档显示标题或格式描述。</summary>
    public required string Title { get; init; }

    /// <summary>当前归档的实际文件格式。</summary>
    public PakArchiveFormat Format { get; init; } = PakArchiveFormat.GeePak3;

    /// <summary>当前归档是否允许通过现有 GEEPAK3 写回链路修改。</summary>
    public bool CanWrite { get; init; } = true;

    /// <summary>打开归档时使用的密码。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>打开和保存归档使用的精确密钥。</summary>
    public PakKeyProfile? KeyProfile { get; init; }

    /// <summary>签名后的两个保留字节。</summary>
    public byte[] ReservedBytes { get; init; } = [];

    /// <summary>解密后的 256 字节全局头，用于保留未知字段。</summary>
    public byte[] PlainGlobalHeader { get; init; } = [];

    /// <summary>按逻辑索引排列的全部槽位。</summary>
    public required List<PakEntry> Slots { get; init; }

    /// <summary>当前非空图片数量。</summary>
    public int ImageCount => Slots.Count(entry => !entry.IsEmpty);
}
