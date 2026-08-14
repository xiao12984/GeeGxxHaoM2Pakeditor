namespace GeePakEditor.Models;

/// <summary>
/// 图片载荷的格式化读取策略。
/// </summary>
public enum PakPayloadMode
{
    /// <summary>项目 GEE 图片使用严格的原始像素长度。</summary>
    Standard,

    /// <summary>
    /// xiami M2Zip 图片使用可见像素前缀；解压流后附带的辅助字节由参考实现忽略。
    /// </summary>
    M2Zip
}
