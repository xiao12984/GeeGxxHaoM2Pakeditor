namespace GeePakEditor.Services;

/// <summary>
/// 表示 PAK 签名、密钥或内部结构验证失败。
/// </summary>
public sealed class PakFormatException : Exception
{
    /// <summary>使用明确的格式错误消息创建异常。</summary>
    public PakFormatException(string message)
        : base(message)
    {
    }

    /// <summary>使用内部异常创建格式异常。</summary>
    public PakFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
