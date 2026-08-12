using System.Text;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// 读取和维护“PAK 完整路径|密码”格式的 FilePassword.txt。
/// </summary>
public sealed class PakPasswordService
{
    private const string PasswordFileName = "FilePassword.txt";

    /// <summary>
    /// 按完整路径优先、同名文件次之的规则查找密码。
    /// </summary>
    public string? ResolvePassword(string pakPath)
    {
        var resolvedPakPath = Path.GetFullPath(pakPath);
        var records = GetCandidateFiles(resolvedPakPath)
            .Where(File.Exists)
            .SelectMany(ReadRecords)
            .ToList();

        var exactMatches = records
            .Where(record => PathsEqual(ResolveConfiguredPath(record), resolvedPakPath))
            .Select(record => record.Password)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (exactMatches.Count == 1)
        {
            return exactMatches[0];
        }

        var fileName = Path.GetFileName(resolvedPakPath);
        var nameMatches = records
            .Where(record => string.Equals(Path.GetFileName(record.FilePath), fileName, StringComparison.OrdinalIgnoreCase))
            .Select(record => record.Password)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return nameMatches.Count == 1 ? nameMatches[0] : null;
    }

    /// <summary>
    /// 在 PAK 同目录的 FilePassword.txt 中新增或更新完整路径记录。
    /// </summary>
    public void SavePassword(string pakPath, string password)
    {
        var resolvedPakPath = Path.GetFullPath(pakPath);
        var configPath = Path.Combine(Path.GetDirectoryName(resolvedPakPath)!, PasswordFileName);
        var existingLines = File.Exists(configPath)
            ? DecodeText(File.ReadAllBytes(configPath)).Split(["\r\n", "\n"], StringSplitOptions.None).ToList()
            : [];

        var replacement = $"{resolvedPakPath}|{password}";
        var replaced = false;
        for (var index = 0; index < existingLines.Count; index++)
        {
            if (!TryParseLine(existingLines[index], configPath, out var record) || record is null)
            {
                continue;
            }

            if (!PathsEqual(ResolveConfiguredPath(record), resolvedPakPath))
            {
                continue;
            }

            existingLines[index] = replacement;
            replaced = true;
            break;
        }

        if (!replaced)
        {
            existingLines.RemoveAll(string.IsNullOrWhiteSpace);
            existingLines.Add(replacement);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(configPath, string.Join(Environment.NewLine, existingLines) + Environment.NewLine, Encoding.GetEncoding(936));
    }

    /// <summary>
    /// 返回最靠近 PAK 的密码文件，并补充应用目录与当前目录候选。
    /// </summary>
    private static IEnumerable<string> GetCandidateFiles(string pakPath)
    {
        return new[]
            {
                Path.Combine(Path.GetDirectoryName(pakPath)!, PasswordFileName),
                Path.Combine(AppContext.BaseDirectory, PasswordFileName),
                Path.Combine(Environment.CurrentDirectory, PasswordFileName)
            }
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取一个密码文件中的有效记录，忽略空行与井号注释。
    /// </summary>
    private static IEnumerable<PakPasswordRecord> ReadRecords(string configPath)
    {
        var text = DecodeText(File.ReadAllBytes(configPath));
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (TryParseLine(line, configPath, out var record) && record is not null)
            {
                yield return record;
            }
        }
    }

    /// <summary>
    /// 解析“路径|密码”，密码内容允许继续包含竖线。
    /// </summary>
    private static bool TryParseLine(string line, string sourceFile, out PakPasswordRecord? record)
    {
        record = null;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        var separator = trimmed.IndexOf('|');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        var configuredPath = trimmed[..separator].Trim().Trim('"', '\'');
        var password = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
        if (configuredPath.Length == 0 || password.Length == 0)
        {
            return false;
        }

        record = new PakPasswordRecord
        {
            FilePath = configuredPath,
            Password = password,
            SourceFile = sourceFile
        };
        return true;
    }

    /// <summary>
    /// 相对路径以密码文件所在目录为基准转换为完整路径。
    /// </summary>
    private static string ResolveConfiguredPath(PakPasswordRecord record)
    {
        if (Path.IsPathFullyQualified(record.FilePath))
        {
            return Path.GetFullPath(record.FilePath);
        }

        return Path.GetFullPath(record.FilePath, Path.GetDirectoryName(record.SourceFile)!);
    }

    /// <summary>
    /// Windows 路径比较不区分大小写。
    /// </summary>
    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 优先识别 UTF-8；失败时按原编辑器常见的 GBK 文本读取。
    /// </summary>
    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }
}
