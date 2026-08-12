namespace GeePakEditor.Models;

/// <summary>
/// 已解密的 GEEPAK3 归档及当前编辑状态。
/// </summary>
public sealed class PakArchive
{
    /// <summary>当前来源文件路径。</summary>
    public required string FilePath { get; set; }

    /// <summary>全局头中的标题。</summary>
    public required string Title { get; init; }

    /// <summary>打开归档时使用的密码。</summary>
    public required string Password { get; init; }

    /// <summary>打开和保存归档使用的精确密钥。</summary>
    public required PakKeyProfile KeyProfile { get; init; }

    /// <summary>签名后的两个保留字节。</summary>
    public required byte[] ReservedBytes { get; init; }

    /// <summary>解密后的 256 字节全局头，用于保留未知字段。</summary>
    public required byte[] PlainGlobalHeader { get; init; }

    /// <summary>按逻辑索引排列的全部槽位。</summary>
    public required List<PakEntry> Slots { get; init; }

    /// <summary>当前非空图片数量。</summary>
    public int ImageCount => Slots.Count(entry => !entry.IsEmpty);
}
