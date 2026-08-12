using System.Text.Json;
using System.IO;
using GeePakEditor.Config;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// 从内置配置、兼容 JSON 和本机离线派生引擎获取 GEEPAK3 密钥。
/// </summary>
public sealed class PakKeyProvider : IPakKeyProvider, IDisposable
{
    private readonly PakBridgeKeyDerivationService _bridgeKeyDerivationService = new();

    /// <inheritdoc />
    public string ProfileFilePath => Path.Combine(AppContext.BaseDirectory, "PakKeyProfiles.json");

    /// <inheritdoc />
    public bool TryGetProfile(string password, out PakKeyProfile? profile)
    {
        if (string.Equals(password, GeePakConstants.DefaultPassword, StringComparison.Ordinal))
        {
            profile = CreateDefaultProfile();
            return true;
        }

        foreach (var candidate in GetProfileCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var configured = ReadProfiles(candidate)
                .FirstOrDefault(item => string.Equals(item.Password, password, StringComparison.Ordinal));
            if (configured is null)
            {
                continue;
            }

            profile = CreateConfiguredProfile(configured, candidate);
            return true;
        }

        // 非默认密码由本机离线引擎自动派生，避免要求用户手动填写 Base64 密钥。
        if (_bridgeKeyDerivationService.TryDerive(password, out profile, out var error))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new PakFormatException(error);
        }

        profile = null;
        return false;
    }

    /// <summary>
    /// 释放当前编辑器启动的本地密码派生进程。
    /// </summary>
    public void Dispose()
    {
        _bridgeKeyDerivationService.Dispose();
    }

    /// <summary>
    /// 返回应用目录与当前目录中的密钥配置候选，避免重复读取同一路径。
    /// </summary>
    private IEnumerable<string> GetProfileCandidates()
    {
        return new[]
            {
                ProfileFilePath,
                Path.Combine(Environment.CurrentDirectory, "PakKeyProfiles.json")
            }
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取 JSON 配置；配置损坏时抛出带路径的明确错误。
    /// </summary>
    private static IReadOnlyList<PakKeyProfileRecord> ReadProfiles(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<PakKeyProfileFile>(json, options)?.Profiles ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PakFormatException($"读取密钥配置失败：{filePath}", exception);
        }
    }

    /// <summary>
    /// 创建公开默认密码对应的内置密钥。
    /// </summary>
    private static PakKeyProfile CreateDefaultProfile()
    {
        return new PakKeyProfile
        {
            Password = GeePakConstants.DefaultPassword,
            IndexKey = Convert.FromBase64String(DefaultPakKeyData.IndexKeyBase64),
            GlobalHeaderKey = Convert.FromBase64String(DefaultPakKeyData.GlobalHeaderKeyBase64),
            ImageHeaderKey = Convert.FromBase64String(DefaultPakKeyData.ImageHeaderKeyBase64),
            Source = "内置默认密钥"
        };
    }

    /// <summary>
    /// 解码并验证外部密钥记录的固定长度。
    /// </summary>
    private static PakKeyProfile CreateConfiguredProfile(PakKeyProfileRecord record, string source)
    {
        try
        {
            var indexKey = Convert.FromBase64String(record.IndexKey);
            var globalKey = Convert.FromBase64String(record.GlobalHeaderKey);
            var imageKey = Convert.FromBase64String(record.ImageHeaderKey);
            ValidateLength(indexKey, 256, "IndexKey", source);
            ValidateLength(globalKey, 256, "GlobalHeaderKey", source);
            ValidateLength(imageKey, 1024, "ImageHeaderKey", source);
            return new PakKeyProfile
            {
                Password = record.Password,
                IndexKey = indexKey,
                GlobalHeaderKey = globalKey,
                ImageHeaderKey = imageKey,
                Source = source
            };
        }
        catch (FormatException exception)
        {
            throw new PakFormatException($"密钥配置包含无效 Base64：{source}", exception);
        }
    }

    /// <summary>
    /// 保证配置密钥与 GEEPAK3 固定密钥表长度一致。
    /// </summary>
    private static void ValidateLength(byte[] value, int expected, string name, string source)
    {
        if (value.Length != expected)
        {
            throw new PakFormatException($"{source} 中 {name} 长度为 {value.Length}，预期 {expected} 字节。");
        }
    }
}
