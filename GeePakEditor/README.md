# GEE PAK 编辑器

这是根据 `gxx Wzl编辑器.exe` 的文件访问行为和 GEEPAK3 样本格式重建的 C# WinForms 编辑器。它不是原 Delphi 源码的反编译结果，而是行为兼容实现。

## 支持范围

- 打开并校验 GEEPAK3。
- 打开并写回传统 `.wzl` + 同名 `.wzx` 资源，可浏览、预览和导出 PNG。
- 按逻辑索引浏览、搜索和预览图片。
- 解码调色板、R5G6B5、RGB24、RGB24+A8、XRGB32、ARGB32。
- 新建 PAK 或 WZL/WZX 资源文件。
- 导入、替换、删除、导出 PNG。
- 修改 X/Y 偏移并保存或另存为 PAK 或 WZL/WZX。
- 自动读取 `FilePassword.txt` 的 `完整路径|密码` 记录。
- 未修改图片保持原始载荷，新增和替换图片写为 ARGB32。
- 传统 WZL/WZX 支持导入、替换、删除、坐标修改和写回。

## 密码和密钥

GEEPAK3 的密码会先派生三组密钥。当前项目内置公开默认密码 `QQ1167746` 的密钥；其他密码会自动调用本机离线派生引擎，不需要手工填写 Base64 密钥。`FilePassword.txt` 继续保存 `完整路径|密码`，并在下次打开时作为预填值。

离线引擎按 `GEE_PAK_BRIDGE_VSIX` 环境变量、程序目录 `PakBridgeSource\boo-ngom-editor.vsix`、当前目录同路径和系统临时目录的顺序查找。首次使用只会解压到当前用户的本地缓存目录，第三方运行文件不会进入源码仓库。

`PakKeyProfiles.json` 仅用于兼容已有的精确密钥配置：

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
- DevExpress WinForms 23.2.6
- `System.Text.Encoding.CodePages` 用于兼容 GBK 密码文件

DevExpress 包需要已配置并具有授权的 DevExpress NuGet 程序包源。
