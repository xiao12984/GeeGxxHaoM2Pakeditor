# GeeGxxHaoM2PakEditor

基于 C# 重建的 GEEPAK3 图片资源编辑器，参考 `gxx Wzl编辑器.exe` 的文件访问行为和样本格式实现。项目目标是提供可审阅、可维护的 GEE PAK 读取、预览与写回链路，并非原 Delphi 工程源码的逐行反编译结果。

## 功能

- 打开并校验 GEEPAK3 归档。
- 按逻辑索引浏览、搜索和预览图片。
- 解码固定调色板、R5G6B5、RGB24、RGB24+A8、XRGB32 和 ARGB32。
- 导入、替换、删除图片，导出为 PNG。
- 修改图片 X/Y 绘制偏移。
- 保存或另存为 GEEPAK3，重新生成加密索引和图片块头。
- 未修改图片保留原始压缩载荷和未识别块头字段。
- 支持 `FilePassword.txt` 的 `完整路径|密码` 配置格式。
- 支持默认密钥和外部 `PakKeyProfiles.json` 精确密钥配置。

## 界面与技术栈

- .NET 8 Windows
- C# 12
- WinForms
- DevExpress WinForms 23.2
- `System.Text.Encoding.CodePages`，用于兼容 GBK 密码文件

工程入口：[GeePakEditor.sln](GeePakEditor.sln)

## 密码配置

原编辑器使用以下文本格式保存 PAK 路径和明文密码：

```text
D:\你的资源目录\示例.Pak|你的密码
```

可参考 [FilePassword.example.txt](GeePakEditor/FilePassword.example.txt)。程序按完整路径优先匹配，路径比较不区分大小写；没有精确路径记录时，会尝试匹配唯一的同名文件记录。

## 派生密钥

GEEPAK3 密码需要派生三组密钥：

- 256 字节索引密钥
- 256 字节全局头密钥
- 1024 字节图片块头密钥

项目内置公开默认密码 `QQ1167746` 对应的密钥。其他密码需要复制 [PakKeyProfiles.example.json](GeePakEditor/PakKeyProfiles.example.json) 为 `GeePakEditor/PakKeyProfiles.json`，再填写精确的 Base64 密钥：

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

参考程序的任意密码派生入口受 VMProtect 保护，目前尚未恢复为可审计的 C# 算法。缺少精确密钥时，编辑器会拒绝解析，不会猜测索引或覆盖原文件。

## 项目结构

```text
GeePakEditor/
  Config/       格式常量、内置密钥和调色板
  Controllers/  主窗口操作流程
  Models/       归档、图片槽位和密钥模型
  Services/     PAK 读写、图片编解码、密码与密钥服务
  Utils/        小端二进制读写与范围校验
  Views/        DevExpress WinForms 界面
analysis/       格式恢复记录与当前边界
```

## 开发环境

1. 安装 .NET 8 SDK。
2. 配置具有授权的 DevExpress NuGet 程序包源。
3. 安装或还原 `DevExpress.Win.Design 23.2.3`。
4. 使用 Visual Studio 2022 打开 `GeePakEditor.sln`。

项目文件已内置 `win-x64`、自包含、单文件发布配置，并关闭裁剪以保证 WinForms 和 DevExpress 兼容性。

## CodeGraph

仓库已初始化 CodeGraph。索引数据库属于本地生成文件，不提交到 Git；克隆仓库后可在仓库根目录执行：

```powershell
codegraph init .
codegraph status .
codegraph explore "GeePakArchiveService Open Save"
```

当前索引覆盖 23 个 C# 文件，可用于查询符号、调用链和修改影响范围。

## 格式记录

- [GEEPAK3 格式恢复记录](analysis/FORMAT.md)
- [重建状态与限制](analysis/STATUS.md)

## 安全说明

- 保存时先写入同目录临时文件，完成后再替换目标文件。
- `.gitignore` 排除参考 EXE、PAK/WZL 资源、真实密码和本机派生密钥。
- 修改重要资源前仍建议保留原始 PAK 备份。
