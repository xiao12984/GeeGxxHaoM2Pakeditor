namespace GeePakEditor.Models;

/// <summary>
/// 外部密钥配置文件的根对象。
/// </summary>
public sealed class PakKeyProfileFile
{
    /// <summary>密码与密钥配置列表。</summary>
    public List<PakKeyProfileRecord> Profiles { get; init; } = [];
}
