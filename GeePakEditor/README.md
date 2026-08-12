# GEE PAK 编辑器

这是根据 `gxx Wzl编辑器.exe` 的文件访问行为和 GEEPAK3 样本格式重建的 C# WinForms 编辑器。它不是原 Delphi 源码的反编译结果，而是行为兼容实现。

## 支持范围

- 打开并校验 GEEPAK3。
- 按逻辑索引浏览、搜索和预览图片。
- 解码调色板、R5G6B5、RGB24、RGB24+A8、XRGB32、ARGB32。
- 导入、替换、删除、导出 PNG。
- 修改 X/Y 偏移并保存或另存为。
- 自动读取 `FilePassword.txt` 的 `完整路径|密码` 记录。
- 未修改图片保持原始载荷，新增和替换图片写为 ARGB32。

## 密码和密钥

GEEPAK3 的密码会先派生三组密钥。当前项目内置公开默认密码 `QQ1167746` 的密钥；其他密码需要复制 `PakKeyProfiles.example.json` 为程序目录中的 `PakKeyProfiles.json`，再配置精确密钥：

```json
{
  "profiles": [
    {
      "password": "你的密码",
      "indexKey": "256 字节密钥的 Base64",
      "globalHeaderKey": "256 字节密钥的 Base64",
      "imageHeaderKey": "1024 字节密钥的 Base64"
    }
  ]
}
```

`FilePassword.txt` 只保存路径和明文密码，不包含派生密钥。例如：

```text
D:\你的资源目录\示例.Pak|你的密码
```

## 项目环境

- .NET 8 Windows / C# 12
- WinForms
- DevExpress WinForms 23.2
- `System.Text.Encoding.CodePages` 用于兼容 GBK 密码文件

DevExpress 包需要已配置并具有授权的 DevExpress NuGet 程序包源。
