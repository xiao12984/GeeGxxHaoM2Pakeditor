using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GeePakEditor.Models;

namespace GeePakEditor.Services;

/// <summary>
/// 调用用户本机已有的离线 PAK 引擎，为非默认密码生成 GEEPAK3 密钥。
/// </summary>
internal sealed class PakBridgeKeyDerivationService : IDisposable
{
    private const string BridgeHost = "127.0.0.1";
    private const int BridgePort = 8765;
    private const string BridgeExecutableName = "boo-pak-bridge.exe";
    private const string BridgeVsixFileName = "boo-ngom-editor.vsix";
    private const string BridgeArchiveRoot = "extension/tools/PakBridge/bin/";
    private static readonly string[] RequiredBridgeFiles =
    [
        BridgeExecutableName,
        "python312.dll",
        "vcruntime140.dll",
        "vcruntime140_1.dll",
        "geepak3_vm_snapshot.zip",
        "lib\\library.dat",
        "lib\\library.zip"
    ];
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly object _syncRoot = new();
    private Process? _ownedBridge;

    /// <summary>
    /// 使用本机离线派生引擎获取指定密码的三组 GEEPAK3 密钥。
    /// </summary>
    public bool TryDerive(string password, out PakKeyProfile? profile, out string? error)
    {
        profile = null;
        error = null;
        try
        {
            EnsureBridgeReady();
            var response = RequestProfile(password);
            profile = CreateProfile(password, response.Profile);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception or JsonException or PakFormatException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// 仅终止由当前编辑器启动的桥接进程，不影响其他程序的本地服务。
    /// </summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_ownedBridge is null)
            {
                return;
            }

            try
            {
                if (!_ownedBridge.HasExited)
                {
                    _ownedBridge.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
                // 进程已退出时无需额外处理。
            }
            finally
            {
                _ownedBridge.Dispose();
                _ownedBridge = null;
            }
        }
    }

    /// <summary>
    /// 确保回环地址上的离线引擎可用；端口无服务时从本机 VSIX 解压并启动。
    /// </summary>
    private void EnsureBridgeReady()
    {
        if (TryProbeBridge(out var bridgeReachable))
        {
            return;
        }

        if (bridgeReachable)
        {
            throw new PakFormatException($"本机端口 {BridgePort} 已被非兼容服务占用，无法启动 GEEPAK3 密码派生引擎。");
        }

        lock (_syncRoot)
        {
            if (TryProbeBridge(out bridgeReachable))
            {
                return;
            }

            if (bridgeReachable)
            {
                throw new PakFormatException($"本机端口 {BridgePort} 已被非兼容服务占用，无法启动 GEEPAK3 密码派生引擎。");
            }

            StartBridge();
        }
    }

    /// <summary>
    /// 检查本机服务是否为支持离线密码派生的桥接引擎。
    /// </summary>
    private static bool TryProbeBridge(out bool reachable)
    {
        reachable = false;
        try
        {
            using var response = HttpClient.GetAsync($"http://{BridgeHost}:{BridgePort}/api/health").GetAwaiter().GetResult();
            reachable = true;
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var health = JsonSerializer.Deserialize<PakBridgeHealthResponse>(json, JsonOptions);
            return health is { Ok: true } && string.Equals(health.Engine, "offline", StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (JsonException)
        {
            reachable = true;
            return false;
        }
    }

    /// <summary>
    /// 从用户本机已有的 VSIX 取得离线引擎文件，再以隐藏窗口方式启动服务。
    /// </summary>
    private void StartBridge()
    {
        var bridgeDirectory = EnsureBridgeFiles();
        var bridgePath = Path.Combine(bridgeDirectory, BridgeExecutableName);
        var environmentPath = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
        var startInfo = new ProcessStartInfo
        {
            FileName = bridgePath,
            Arguments = $"serve --host {BridgeHost} --port {BridgePort}",
            WorkingDirectory = bridgeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["Path"] = string.Join(
            Path.PathSeparator,
            new[] { bridgeDirectory, Path.Combine(bridgeDirectory, "lib"), environmentPath }.Where(value => value.Length > 0));

        try
        {
            _ownedBridge = Process.Start(startInfo)
                ?? throw new PakFormatException("无法启动本机 GEEPAK3 密码派生引擎。");

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (TryProbeBridge(out var reachable) && reachable)
                {
                    return;
                }

                if (_ownedBridge.HasExited)
                {
                    throw new PakFormatException($"GEEPAK3 密码派生引擎启动失败，退出码：{_ownedBridge.ExitCode}。");
                }

                Thread.Sleep(200);
            }

            throw new PakFormatException("等待 GEEPAK3 密码派生引擎启动超时。");
        }
        catch
        {
            // 启动失败时立即回收本次启动的子进程，避免遗留端口占用。
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// 返回缓存目录中的桥接文件；首次使用时只从本机 VSIX 解压，不下载或提交第三方二进制文件。
    /// </summary>
    private static string EnsureBridgeFiles()
    {
        var bridgeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeePakEditor",
            "PakBridge");
        var bridgePath = Path.Combine(bridgeDirectory, BridgeExecutableName);
        if (File.Exists(bridgePath))
        {
            ValidateBridgeRuntime(bridgeDirectory);
            return bridgeDirectory;
        }

        var vsixPath = ResolveBridgeVsixPath();
        Directory.CreateDirectory(bridgeDirectory);
        using var archive = ZipFile.OpenRead(vsixPath);
        var entries = archive.Entries
            .Where(entry => entry.FullName.StartsWith(BridgeArchiveRoot, StringComparison.OrdinalIgnoreCase) && entry.Length > 0)
            .ToList();
        if (entries.Count == 0)
        {
            throw new PakFormatException($"离线引擎包不包含 {BridgeArchiveRoot}：{vsixPath}");
        }

        foreach (var entry in entries)
        {
            var relativePath = entry.FullName[BridgeArchiveRoot.Length..].Replace('/', Path.DirectorySeparatorChar);
            var outputPath = Path.GetFullPath(Path.Combine(bridgeDirectory, relativePath));
            var expectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bridgeDirectory)) + Path.DirectorySeparatorChar;
            if (!outputPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new PakFormatException("离线引擎包包含不安全的文件路径。");
            }

            if (File.Exists(outputPath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var source = entry.Open();
            using var target = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }

        if (!File.Exists(bridgePath))
        {
            throw new PakFormatException($"离线引擎解压不完整，未找到：{bridgePath}");
        }

        ValidateBridgeRuntime(bridgeDirectory);
        return bridgeDirectory;
    }

    /// <summary>
    /// 检查启动桥接程序所需的核心运行库，避免以难以理解的 DLL 错误结束。
    /// </summary>
    private static void ValidateBridgeRuntime(string bridgeDirectory)
    {
        var missingFiles = RequiredBridgeFiles
            .Where(relativePath => !File.Exists(Path.Combine(bridgeDirectory, relativePath)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new PakFormatException($"本机 GEEPAK3 密码派生引擎不完整，缺少：{string.Join("、", missingFiles)}。");
        }
    }

    /// <summary>
    /// 按环境变量、程序目录、当前目录和系统临时目录的顺序查找用户已有的桥接 VSIX。
    /// </summary>
    private static string ResolveBridgeVsixPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("GEE_PAK_BRIDGE_VSIX");
        var candidates = new[]
            {
                configuredPath,
                Path.Combine(AppContext.BaseDirectory, "PakBridgeSource", BridgeVsixFileName),
                Path.Combine(Environment.CurrentDirectory, "PakBridgeSource", BridgeVsixFileName),
                Path.Combine(Path.GetTempPath(), BridgeVsixFileName)
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var vsixPath = candidates.FirstOrDefault(File.Exists);
        if (vsixPath is not null)
        {
            return vsixPath;
        }

        throw new PakFormatException(
            "未找到本机 GEEPAK3 密码派生引擎。请将已授权的 boo-ngom-editor.vsix 放到程序目录的 PakBridgeSource 文件夹，或设置环境变量 GEE_PAK_BRIDGE_VSIX。");
    }

    /// <summary>
    /// 向离线引擎发送 UTF-8 密码，并读取其返回的 Base64 密钥配置。
    /// </summary>
    private static PakBridgeProfileResponse RequestProfile(string password)
    {
        var body = JsonSerializer.Serialize(new PakBridgePasswordRequest { Password = password });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{BridgeHost}:{BridgePort}/api/gee-profile")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var response = HttpClient.Send(request);
        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new PakFormatException($"GEEPAK3 密码派生引擎返回 HTTP {(int)response.StatusCode}：{responseJson}");
        }

        return JsonSerializer.Deserialize<PakBridgeProfileResponse>(responseJson, JsonOptions)
            ?? throw new PakFormatException("GEEPAK3 密码派生引擎返回了空结果。");
    }

    /// <summary>
    /// 解码桥接响应并校验三组密钥的固定长度。
    /// </summary>
    private static PakKeyProfile CreateProfile(string password, PakBridgeProfile? bridgeProfile)
    {
        if (bridgeProfile is null || string.IsNullOrWhiteSpace(bridgeProfile.IndexKey) || string.IsNullOrWhiteSpace(bridgeProfile.GlobalHeaderKey) || string.IsNullOrWhiteSpace(bridgeProfile.ImageHeaderKey))
        {
            throw new PakFormatException("GEEPAK3 密码派生引擎返回的密钥字段不完整。");
        }

        try
        {
            var indexKey = Convert.FromBase64String(bridgeProfile.IndexKey);
            var globalHeaderKey = Convert.FromBase64String(bridgeProfile.GlobalHeaderKey);
            var imageHeaderKey = Convert.FromBase64String(bridgeProfile.ImageHeaderKey);
            ValidateLength(indexKey, 256, "indexKey");
            ValidateLength(globalHeaderKey, 256, "globalHeaderKey");
            ValidateLength(imageHeaderKey, 1024, "imageHeaderKey");
            return new PakKeyProfile
            {
                Password = password,
                IndexKey = indexKey,
                GlobalHeaderKey = globalHeaderKey,
                ImageHeaderKey = imageHeaderKey,
                Source = "本机离线密码派生引擎"
            };
        }
        catch (FormatException exception)
        {
            throw new PakFormatException("GEEPAK3 密码派生引擎返回了无效的 Base64 密钥。", exception);
        }
    }

    /// <summary>
    /// 保证桥接返回的密钥表能与当前 GEEPAK3 读写规则对应。
    /// </summary>
    private static void ValidateLength(byte[] value, int expectedLength, string name)
    {
        if (value.Length != expectedLength)
        {
            throw new PakFormatException($"GEEPAK3 密码派生引擎返回的 {name} 长度为 {value.Length}，预期 {expectedLength} 字节。");
        }
    }

    /// <summary>
    /// 描述桥接健康检查响应的最小字段集。
    /// </summary>
    private sealed class PakBridgeHealthResponse
    {
        /// <summary>服务是否已准备就绪。</summary>
        public bool Ok { get; init; }

        /// <summary>服务实现类型。</summary>
        public string? Engine { get; init; }
    }

    /// <summary>
    /// 描述密码派生请求。
    /// </summary>
    private sealed class PakBridgePasswordRequest
    {
        /// <summary>待派生的原始密码。</summary>
        public string Password { get; init; } = string.Empty;
    }

    /// <summary>
    /// 描述密码派生接口的外层响应。
    /// </summary>
    private sealed class PakBridgeProfileResponse
    {
        /// <summary>GEEPAK3 密钥配置。</summary>
        public PakBridgeProfile? Profile { get; init; }
    }

    /// <summary>
    /// 描述桥接返回的三组 Base64 密钥。
    /// </summary>
    private sealed class PakBridgeProfile
    {
        /// <summary>索引异或密钥。</summary>
        public string? IndexKey { get; init; }

        /// <summary>全局头异或密钥。</summary>
        public string? GlobalHeaderKey { get; init; }

        /// <summary>图片头异或密钥。</summary>
        public string? ImageHeaderKey { get; init; }
    }
}
