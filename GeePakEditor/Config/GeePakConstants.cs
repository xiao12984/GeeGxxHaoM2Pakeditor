namespace GeePakEditor.Config;

/// <summary>
/// GEEPAK3 文件结构常量。
/// </summary>
internal static class GeePakConstants
{
    /// <summary>
    /// GEEPAK3 固定文件签名。
    /// </summary>
    public static readonly byte[] Signature = [0x07, 0x47, 0x45, 0x45, 0x50, 0x41, 0x4B, 0x33];

    /// <summary>
    /// 签名、保留字段与加密全局头的总长度。
    /// </summary>
    public const int HeaderSize = 266;

    /// <summary>
    /// 加密全局头长度。
    /// </summary>
    public const int GlobalHeaderSize = 256;

    /// <summary>
    /// 单个图片块头长度。
    /// </summary>
    public const int ImageHeaderSize = 16;

    /// <summary>
    /// 传统 WZX 索引文件头部长度。
    /// </summary>
    public const int WzxHeaderSize = 48;

    /// <summary>
    /// 传统 WZL 数据文件头部长度。
    /// </summary>
    public const int WzlHeaderSize = 64;

    /// <summary>
    /// 防止损坏文件触发过量内存分配的槽位上限。
    /// </summary>
    public const int MaximumSlotCount = 1_000_000;

    /// <summary>
    /// 原编辑器常见的默认密码。
    /// </summary>
    public const string DefaultPassword = "QQ1167746";
}
