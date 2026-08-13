# 资源格式分析工具

`Analyze-ResourceFiles.ps1` 是一个只读的批量分析脚本，用于先扫描客户端资源，再决定哪些文件需要补专用 Reader。

## 扫描 Data 目录

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Analyze-ResourceFiles.ps1 `
  -Path "H:\Mir2客户端\初心者客户端\Data" `
  -Recurse `
  -CsvPath ".\analysis\resource-report.csv"
```

## 只查看问题文件

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Analyze-ResourceFiles.ps1 `
  -Path "H:\Mir2客户端\初心者客户端\Data" `
  -Recurse `
  -OnlyProblems
```

## 从任意目录运行

```powershell
pwsh -ExecutionPolicy Bypass -File "D:\code\GEE PAK\tools\Analyze-ResourceFiles.ps1" `
  -Path "H:\Mir2客户端\初心者客户端\Data" `
  -Recurse `
  -OnlyProblems `
  -CsvPath "D:\code\GEE PAK\analysis\resource-report.csv"
```

当前脚本会：

- 批量检查 `.wzl/.wzx` 的配对关系、WZX 槽位数量和偏移表。
- 校验 WZL 图片块的尺寸、边界、载荷长度和 zlib 头。
- 识别纯 `Encode=3/5` 的 M2Zip 候选，并标记为只读。
- 识别当前编辑器支持的 3/5/6/7 图片布局候选。
- 盘点 `.wil/.wix`、`.wis`、`.mix` 文件，并明确标记为等待专用 Reader。
- 支持控制台查看和 CSV 导出，避免逐个手动打开测试。
- 按 xiami 的 `IndexCount` 读取 WZX 前 N 个偏移，尾部多出的偏移表项会作为提示保留在报告中。
- `-OnlyProblems` 同时保留结构错误和 `ExtraIndexEntries` 兼容性警告，避免可读取但可能触发旧版主程序报错的文件被隐藏。
- 默认每个文件最多记录 20 条明细问题，可通过 `-MaxIssueDetails` 调整。
