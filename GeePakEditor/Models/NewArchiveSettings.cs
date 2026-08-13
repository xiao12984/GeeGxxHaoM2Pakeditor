namespace GeePakEditor.Models;

/// <summary>
/// 新建资源归档时由对话框返回的格式与密码设置。
/// </summary>
public sealed class NewArchiveSettings
{
    /// <summary>用户选择的新归档格式。</summary>
    public required PakArchiveFormat Format { get; init; }

    /// <summary>新建 GEEPAK3 时使用的明文密码；WZL 不使用该字段。</summary>
    public string Password { get; init; } = string.Empty;
}
