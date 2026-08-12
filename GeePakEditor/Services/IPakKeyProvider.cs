using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// 按密码查找精确 GEEPAK3 密钥的服务。
/// </summary>
public interface IPakKeyProvider
{
    /// <summary>尝试从内置表或外部配置解析密钥。</summary>
    bool TryGetProfile(string password, out PakKeyProfile? profile);

    /// <summary>外部密钥配置文件路径。</summary>
    string ProfileFilePath { get; }
}
