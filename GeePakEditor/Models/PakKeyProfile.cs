namespace GeePakEditor.Models;

/// <summary>
/// 一组可精确解密并重新加密 GEEPAK3 的密钥。
/// </summary>
public sealed class PakKeyProfile
{
    /// <summary>配置对应的明文密码。</summary>
    public required string Password { get; init; }

    /// <summary>256 字节索引密钥。</summary>
    public required byte[] IndexKey { get; init; }

    /// <summary>256 字节全局头密钥。</summary>
    public required byte[] GlobalHeaderKey { get; init; }

    /// <summary>1024 字节图片块头密钥。</summary>
    public required byte[] ImageHeaderKey { get; init; }

    /// <summary>密钥来源，用于错误定位与状态显示。</summary>
    public required string Source { get; init; }
}
