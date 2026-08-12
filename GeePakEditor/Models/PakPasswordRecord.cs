namespace GeePakEditor.Models;

/// <summary>
/// FilePassword.txt 中的一条路径与密码记录。
/// </summary>
public sealed class PakPasswordRecord
{
    /// <summary>配置文件中记录的 PAK 路径。</summary>
    public required string FilePath { get; init; }

    /// <summary>该 PAK 使用的明文密码。</summary>
    public required string Password { get; init; }

    /// <summary>记录来源的密码文件路径。</summary>
    public required string SourceFile { get; init; }
}
