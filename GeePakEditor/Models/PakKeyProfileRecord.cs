namespace GeePakEditor.Models;

/// <summary>
/// 外部 JSON 中的一条 Base64 密钥记录。
/// </summary>
public sealed class PakKeyProfileRecord
{
    /// <summary>密钥对应的密码。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Base64 编码的索引密钥。</summary>
    public string IndexKey { get; init; } = string.Empty;

    /// <summary>Base64 编码的全局头密钥。</summary>
    public string GlobalHeaderKey { get; init; } = string.Empty;

    /// <summary>Base64 编码的图片块头密钥。</summary>
    public string ImageHeaderKey { get; init; } = string.Empty;
}
