[CmdletBinding()]
param(
    # 待分析的客户端 Data 目录或单个资源文件。
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path,

    # 是否递归扫描子目录。
    [switch]$Recurse,

    # 可选的 CSV 输出路径。
    [string]$CsvPath,

    # 只显示异常、未配对或尚未实现专用 Reader 的文件。
    [switch]$OnlyProblems,

    # 每个文件最多写入多少条明细问题，避免大资源报告过度膨胀。
    [int]$MaxIssueDetails = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:WzxHeaderSize = 48
$script:WzlHeaderSize = 64
$script:ImageHeaderSize = 16
$script:MaximumSlotCount = 1000000
$script:MaxIssueDetails = [Math]::Max(1, $MaxIssueDetails)

function Read-UInt16LE {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) {
        throw "读取 UInt16 越界：偏移 $Offset，文件长度 $($Bytes.Length)。"
    }

    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-Int16LE {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) {
        throw "读取 Int16 越界：偏移 $Offset，文件长度 $($Bytes.Length)。"
    }

    return [BitConverter]::ToInt16($Bytes, $Offset)
}

function Read-UInt32LE {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) {
        throw "读取 UInt32 越界：偏移 $Offset，文件长度 $($Bytes.Length)。"
    }

    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Get-Align4 {
    param(
        [long]$Value
    )

    return [long]([Math]::Ceiling($Value / 4.0) * 4)
}

function Get-RawSize {
    param(
        [byte]$ImageType,
        [byte]$Flags,
        [int]$Width,
        [int]$Height
    )

    if ($Width -lt 1 -or $Width -gt 4096 -or $Height -lt 1 -or $Height -gt 4096) {
        return $null
    }

    $rowSize = $null
    switch ("{0}:{1}" -f $ImageType, $Flags) {
        '3:0' { $rowSize = Get-Align4 $Width; break }
        '5:0' { $rowSize = Get-Align4 ($Width * 2); break }
        '6:0' { $rowSize = Get-Align4 ($Width * 3); break }
        '6:1' { $rowSize = (Get-Align4 ($Width * 3)) + (Get-Align4 $Width); break }
        '7:0' { $rowSize = $Width * 4; break }
        '7:1' { $rowSize = $Width * 4; break }
        default { return $null }
    }

    return [long]$rowSize * $Height
}

function Test-ZlibHeader {
    param(
        [byte[]]$Bytes,
        [long]$Offset
    )

    if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) {
        return $false
    }

    # PowerShell 对 byte 直接左移可能保留 byte 宽度，先转成 int 才能正确验证 78 DA 这类合法 zlib 头。
    $cmf = [int]$Bytes[[int]$Offset]
    $flg = [int]$Bytes[[int]$Offset + 1]
    return (($cmf -band 0x0F) -eq 8) -and ((($cmf -shl 8) + $flg) % 31 -eq 0)
}

function Add-LimitedIssue {
    param(
        [System.Collections.Generic.List[string]]$Issues,
        [string]$Message,
        [ref]$SuppressedCount
    )

    if ($Issues.Count -lt $script:MaxIssueDetails) {
        $Issues.Add($Message)
        return
    }

    $SuppressedCount.Value++
}

function Resolve-SiblingFile {
    param(
        [System.IO.FileInfo]$File,
        [string]$Extension
    )

    $directPath = [System.IO.Path]::ChangeExtension($File.FullName, $Extension)
    if (Test-Path -LiteralPath $directPath -PathType Leaf) {
        return Get-Item -LiteralPath $directPath
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($File.Name)
    return Get-ChildItem -LiteralPath $File.DirectoryName -File | Where-Object {
        [System.IO.Path]::GetFileNameWithoutExtension($_.Name) -ieq $baseName -and
        [System.IO.Path]::GetExtension($_.Name) -ieq $Extension
    } | Select-Object -First 1
}

function New-Result {
    param(
        [string]$DataPath,
        [string]$CompanionPath,
        [string]$Family,
        [string]$Status,
        [string]$Confidence = '',
        [int]$Slots = 0,
        [int]$EmptySlots = 0,
        [int]$Blocks = 0,
        [int]$ValidBlocks = 0,
        [int]$InvalidBlocks = 0,
        [int]$DuplicateRefs = 0,
        [int]$ExtraIndexEntries = 0,
        [string]$Encodes = '',
        [long]$DataBytes = 0,
        [long]$IndexBytes = 0,
        [string]$Issues = ''
    )

    return [pscustomobject][ordered]@{
        Path          = $DataPath
        Companion     = $CompanionPath
        Family        = $Family
        Status        = $Status
        Confidence    = $Confidence
        Slots         = $Slots
        EmptySlots    = $EmptySlots
        Blocks        = $Blocks
        ValidBlocks   = $ValidBlocks
        InvalidBlocks = $InvalidBlocks
        DuplicateRefs = $DuplicateRefs
        ExtraIndexEntries = $ExtraIndexEntries
        Encodes       = $Encodes
        DataBytes     = $DataBytes
        IndexBytes    = $IndexBytes
        Issues        = $Issues
    }
}

function Analyze-WzlPair {
    param(
        [System.IO.FileInfo]$WzlFile
    )

    $wzxFile = Resolve-SiblingFile -File $WzlFile -Extension '.wzx'
    if ($null -eq $wzxFile) {
        return New-Result `
            -DataPath $WzlFile.FullName `
            -CompanionPath '' `
            -Family 'WZL/WZX' `
            -Status '缺少同名 WZX' `
            -Confidence '高' `
            -DataBytes $WzlFile.Length `
            -Issues '未找到同目录、同文件名的 .wzx 索引文件。'
    }

    $issues = New-Object 'System.Collections.Generic.List[string]'
    $validBlocks = 0
    $invalidBlocks = 0
    $emptySlots = 0
    $duplicateRefs = 0
    $extraIndexEntries = 0
    $suppressedIssueCount = 0
    $physicalBlocks = @{}
    $blockTypes = New-Object 'System.Collections.Generic.HashSet[string]'
    $wzlBytes = [System.IO.File]::ReadAllBytes($WzlFile.FullName)
    $wzxBytes = [System.IO.File]::ReadAllBytes($wzxFile.FullName)

    try {
        if ($wzlBytes.Length -lt $script:WzlHeaderSize) {
            throw "WZL 文件长度小于 $($script:WzlHeaderSize) 字节。"
        }

        if ($wzxBytes.Length -lt $script:WzxHeaderSize -or
            (($wzxBytes.Length - $script:WzxHeaderSize) % 4) -ne 0) {
            throw 'WZX 文件长度无效，48 字节头后必须是完整的 UInt32 偏移表。'
        }

        $headerSlotCount = [long](Read-UInt32LE -Bytes $wzxBytes -Offset 44)
        $tableSlotCount = [long](($wzxBytes.Length - $script:WzxHeaderSize) / 4)
        if ($headerSlotCount -gt $tableSlotCount) {
            throw "WZX 偏移表不完整：头部声明=$headerSlotCount，实际表项=$tableSlotCount。"
        }

        if ($headerSlotCount -gt $script:MaximumSlotCount) {
            throw "WZX 槽位数量超过上限 $($script:MaximumSlotCount)。"
        }

        if ($headerSlotCount -lt $tableSlotCount) {
            $extraIndexEntries = [int]($tableSlotCount - $headerSlotCount)
            $issues.Add("WZX 文件尾部存在 $extraIndexEntries 个额外偏移表项；已按 xiami 的 IndexCount 只读取前 $headerSlotCount 项。")
        }

        $slotCount = [int]$headerSlotCount
        $offsets = New-Object 'System.Collections.Generic.List[uint]'
        for ($index = 0; $index -lt $slotCount; $index++) {
            $offset = Read-UInt32LE -Bytes $wzxBytes -Offset ($script:WzxHeaderSize + $index * 4)
            $offsets.Add($offset)

            if ($offset -eq 0) {
                $emptySlots++
                continue
            }

            # 48 是 xiami/M2Zip 常见的空槽哨兵值，与零一样不指向图片块。
            if ($offset -eq $script:WzxHeaderSize) {
                $emptySlots++
                continue
            }

            if ($offset -lt $script:WzlHeaderSize -or
                [long]$offset + $script:ImageHeaderSize -gt $wzlBytes.Length) {
                $invalidBlocks++
                Add-LimitedIssue `
                    -Issues $issues `
                    -Message "槽位 $index 的块偏移 $offset 越界。" `
                    -SuppressedCount ([ref]$suppressedIssueCount)
                continue
            }

            $key = [uint32]$offset
            if ($physicalBlocks.ContainsKey($key)) {
                $duplicateRefs++
                continue
            }

            $headerOffset = [int]$offset
            $imageType = $wzlBytes[$headerOffset]
            $flags = $wzlBytes[$headerOffset + 3]
            $width = [int](Read-UInt16LE -Bytes $wzlBytes -Offset ($headerOffset + 4))
            $height = [int](Read-UInt16LE -Bytes $wzlBytes -Offset ($headerOffset + 6))
            $x = Read-Int16LE -Bytes $wzlBytes -Offset ($headerOffset + 8)
            $y = Read-Int16LE -Bytes $wzlBytes -Offset ($headerOffset + 10)
            $compressedSize = [long](Read-UInt32LE -Bytes $wzlBytes -Offset ($headerOffset + 12))
            [void]$blockTypes.Add([string]$imageType)

            $physicalBlocks[$key] = [pscustomobject]@{
                Offset         = [long]$offset
                Type           = $imageType
                Flags          = $flags
                Width          = $width
                Height         = $height
                X              = $x
                Y              = $y
                CompressedSize = $compressedSize
            }
        }

        $sortedBlocks = @($physicalBlocks.Values | Sort-Object Offset)
        $blocks = $sortedBlocks.Count
        $isM2ZipShape = $blocks -gt 0 -and (@($sortedBlocks | Where-Object { $_.Type -notin @(3, 5) }).Count -eq 0)
        $isGenericShape = $blocks -gt 0 -and (@($sortedBlocks | Where-Object { $_.Type -notin @(3, 5, 6, 7) }).Count -eq 0)

        for ($order = 0; $order -lt $sortedBlocks.Count; $order++) {
            $block = $sortedBlocks[$order]
            $nextOffset = if ($order + 1 -lt $sortedBlocks.Count) {
                $sortedBlocks[$order + 1].Offset
            }
            else {
                [long]$wzlBytes.Length
            }

            # M2Zip 的第 1 至第 3 字节是保留字段，type 3/5 按固定像素布局计算。
            $effectiveFlags = if ($isM2ZipShape) { [byte]0 } else { $block.Flags }
            $rawSize = Get-RawSize `
                -ImageType $block.Type `
                -Flags $effectiveFlags `
                -Width $block.Width `
                -Height $block.Height

            $blockIssues = New-Object 'System.Collections.Generic.List[string]'
            if ($null -eq $rawSize) {
                $blockIssues.Add("类型 $($block.Type)/标志 $($block.Flags) 或尺寸 $($block.Width)x$($block.Height) 不支持。")
            }
            else {
                $payloadSize = if ($block.CompressedSize -eq 0) { [long]$rawSize } else { $block.CompressedSize }
                $payloadOffset = $block.Offset + $script:ImageHeaderSize
                if ($payloadOffset + $payloadSize -gt $wzlBytes.Length) {
                    $blockIssues.Add("载荷超出 WZL 文件末尾。")
                }
                elseif ($payloadOffset + $payloadSize -gt $nextOffset) {
                    $blockIssues.Add("载荷与下一个物理块重叠。")
                }
                elseif ($block.CompressedSize -gt 0 -and -not (Test-ZlibHeader -Bytes $wzlBytes -Offset $payloadOffset)) {
                    $blockIssues.Add('压缩载荷的 zlib 头无效。')
                }
            }

            if ($blockIssues.Count -eq 0) {
                $validBlocks++
            }
            else {
                $invalidBlocks++
                Add-LimitedIssue `
                    -Issues $issues `
                    -Message "块偏移 $($block.Offset)：$($blockIssues -join '、')" `
                    -SuppressedCount ([ref]$suppressedIssueCount)
            }
        }

        if ($suppressedIssueCount -gt 0) {
            $issues.Add("另有 $suppressedIssueCount 条明细问题已省略；请结合 InvalidBlocks、Encodes 和前几条问题定位。")
        }

        $encodes = (@($blockTypes | Sort-Object) -join ',')
        if ($blocks -eq 0) {
            $status = '空 WZL/WZX'
            $confidence = '高'
            $family = 'WZL/WZX'
            $reason = '索引中没有指向图片块的有效槽位。'
        }
        elseif ($invalidBlocks -gt 0) {
            $status = '结构异常'
            $confidence = '高'
            $family = if ($isM2ZipShape) { 'M2Zip 候选' } elseif ($isGenericShape) { 'WZL 候选' } else { '未知 WZL 变体' }
            $reason = '索引或至少一个物理图片块未通过边界、尺寸或 zlib 校验。'
        }
        elseif ($isM2ZipShape) {
            $status = 'M2Zip 候选（只读）'
            $confidence = '高'
            $family = 'M2Zip/WZL-WZX'
            $reason = '所有物理块类型均为 3/5，且索引、尺寸、边界和 zlib 头均通过校验。'
        }
        elseif ($isGenericShape) {
            $status = '项目 WZL 候选（可编辑）'
            $confidence = '中'
            $family = 'WZL/WZX'
            $reason = '物理块类型属于当前编辑器支持的 3/5/6/7 布局，但不是纯 M2Zip。'
        }
        else {
            $status = '未知 WZL 变体'
            $confidence = '高'
            $family = '未知 WZL'
            $reason = '存在当前分析器尚未实现的图片类型或标志组合。'
        }

        if ($reason) {
            $issues.Insert(0, $reason)
        }

        return New-Result `
            -DataPath $WzlFile.FullName `
            -CompanionPath $wzxFile.FullName `
            -Family $family `
            -Status $status `
            -Confidence $confidence `
            -Slots $slotCount `
            -EmptySlots $emptySlots `
            -Blocks $blocks `
            -ValidBlocks $validBlocks `
            -InvalidBlocks $invalidBlocks `
            -DuplicateRefs $duplicateRefs `
            -ExtraIndexEntries $extraIndexEntries `
            -Encodes $encodes `
            -DataBytes $wzlBytes.Length `
            -IndexBytes $wzxBytes.Length `
            -Issues ($issues -join '；')
    }
    catch {
        return New-Result `
            -DataPath $WzlFile.FullName `
            -CompanionPath $wzxFile.FullName `
            -Family 'WZL/WZX' `
            -Status '无法分析' `
            -Confidence '高' `
            -Slots 0 `
            -Blocks $physicalBlocks.Count `
            -ValidBlocks $validBlocks `
            -InvalidBlocks $invalidBlocks `
            -DuplicateRefs $duplicateRefs `
            -ExtraIndexEntries $extraIndexEntries `
            -DataBytes $wzlBytes.Length `
            -IndexBytes $wzxBytes.Length `
            -Issues $_.Exception.Message
    }
}

function New-UnparsedFamilyResult {
    param(
        [System.IO.FileInfo]$File,
        [string]$Family,
        [string]$CompanionExtension = ''
    )

    $companion = ''
    $issue = 'xiami 使用专用 Reader 处理该格式；当前脚本只做文件族盘点，尚未执行完整结构解析。'
    if ($CompanionExtension) {
        $companionFile = Resolve-SiblingFile -File $File -Extension $CompanionExtension
        if ($null -eq $companionFile) {
            $issue = "未找到同名 $CompanionExtension 配套文件；需要结合 xiami 对应 Reader 进一步确认。"
        }
        else {
            $companion = $companionFile.FullName
        }
    }

    return New-Result `
        -DataPath $File.FullName `
        -CompanionPath $companion `
        -Family $Family `
        -Status '待专用 Reader' `
        -Confidence '中' `
        -DataBytes $File.Length `
        -Issues $issue
}

function Get-InputFiles {
    param(
        [string]$InputPath,
        [switch]$ScanRecurse
    )

    $item = Get-Item -LiteralPath $InputPath
    if ($item -is [System.IO.FileInfo]) {
        return @($item)
    }

    return @(Get-ChildItem -LiteralPath $item.FullName -File -Recurse:$ScanRecurse)
}

$inputFiles = Get-InputFiles -InputPath $Path -ScanRecurse:$Recurse
$results = New-Object 'System.Collections.Generic.List[object]'
$handled = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

foreach ($file in $inputFiles) {
    $extension = [System.IO.Path]::GetExtension($file.Name).ToLowerInvariant()
    switch ($extension) {
        '.wzl' {
            $result = Analyze-WzlPair -WzlFile $file
            [void]$results.Add($result)
            [void]$handled.Add($file.FullName)
            $wzx = Resolve-SiblingFile -File $file -Extension '.wzx'
            if ($null -ne $wzx) {
                [void]$handled.Add($wzx.FullName)
            }
            break
        }
        '.wzx' {
            if (-not $handled.Contains($file.FullName)) {
                $wzl = Resolve-SiblingFile -File $file -Extension '.wzl'
                if ($null -eq $wzl) {
                    [void]$results.Add((New-Result `
                        -DataPath $file.FullName `
                        -CompanionPath '' `
                        -Family 'WZL/WZX' `
                        -Status '缺少同名 WZL' `
                        -Confidence '高' `
                        -IndexBytes $file.Length `
                        -Issues '索引文件没有找到同名的 .wzl 数据文件。'))
                }
                else {
                    $result = Analyze-WzlPair -WzlFile $wzl
                    [void]$results.Add($result)
                    [void]$handled.Add($wzl.FullName)
                }

                [void]$handled.Add($file.FullName)
            }
            break
        }
        '.wil' {
            if (-not $handled.Contains($file.FullName)) {
                [void]$results.Add((New-UnparsedFamilyResult -File $file -Family 'M2Def/WIL-WIX' -CompanionExtension '.wix'))
                [void]$handled.Add($file.FullName)
                $wix = Resolve-SiblingFile -File $file -Extension '.wix'
                if ($null -ne $wix) {
                    [void]$handled.Add($wix.FullName)
                }
            }
            break
        }
        '.wix' {
            if (-not $handled.Contains($file.FullName)) {
                $wil = Resolve-SiblingFile -File $file -Extension '.wil'
                if ($null -eq $wil) {
                    [void]$results.Add((New-Result `
                        -DataPath $file.FullName `
                        -CompanionPath '' `
                        -Family 'M2Def/WIL-WIX' `
                        -Status '缺少同名 WIL' `
                        -Confidence '高' `
                        -IndexBytes $file.Length `
                        -Issues '索引文件没有找到同名的 .wil 数据文件。'))
                }
                else {
                    [void]$results.Add((New-UnparsedFamilyResult -File $wil -Family 'M2Def/WIL-WIX' -CompanionExtension '.wix'))
                    [void]$handled.Add($wil.FullName)
                }

                [void]$handled.Add($file.FullName)
            }
            break
        }
        '.wis' {
            if (-not $handled.Contains($file.FullName)) {
                [void]$results.Add((New-UnparsedFamilyResult -File $file -Family 'M2Wis/WIS'))
                [void]$handled.Add($file.FullName)
            }
            break
        }
        '.mix' {
            if (-not $handled.Contains($file.FullName)) {
                [void]$results.Add((New-UnparsedFamilyResult -File $file -Family 'M3Zip/MIX'))
                [void]$handled.Add($file.FullName)
            }
            break
        }
    }
}

$allResults = @($results | Sort-Object Path)
$orderedResults = $allResults
if ($OnlyProblems) {
    $orderedResults = @($orderedResults | Where-Object {
        $_.Status -notin @('M2Zip 候选（只读）', '项目 WZL 候选（可编辑）', '空 WZL/WZX') -or
        [int]$_.ExtraIndexEntries -gt 0
    })
}

$reportColumns = @(
    'Path',
    'Companion',
    'Family',
    'Status',
    'Confidence',
    'Slots',
    'EmptySlots',
    'Blocks',
    'ValidBlocks',
    'InvalidBlocks',
    'DuplicateRefs',
    'ExtraIndexEntries',
    'Encodes',
    'DataBytes',
    'IndexBytes',
    'Issues'
)

if ($CsvPath) {
    $csvFullPath = [System.IO.Path]::GetFullPath($CsvPath)
    $csvDirectory = [System.IO.Path]::GetDirectoryName($csvFullPath)
    if ($csvDirectory) {
        [System.IO.Directory]::CreateDirectory($csvDirectory) | Out-Null
    }

    if ($orderedResults.Count -gt 0) {
        $orderedResults |
            Select-Object $reportColumns |
            Export-Csv -LiteralPath $csvFullPath -NoTypeInformation -Encoding UTF8
    }
    else {
        # 即使问题过滤后没有记录，也写入表头，避免生成 0KB 文件让使用者误判为脚本失败。
        $header = '"' + ($reportColumns -join '","') + '"'
        Set-Content -LiteralPath $csvFullPath -Value $header -Encoding UTF8
    }

    Write-Host "CSV 已写入：$csvFullPath"
}

if ($orderedResults.Count -eq 0) {
    if ($allResults.Count -eq 0) {
        Write-Host '没有找到可分析的资源文件。'
    }
    elseif ($OnlyProblems) {
        Write-Host ("已分析 {0} 个资源项，未发现需要关注的问题项。" -f $allResults.Count)
    }
    else {
        Write-Host '没有可输出的资源项。'
    }

    exit 0
}

$orderedResults |
    Select-Object Path, Family, Status, Slots, Blocks, ValidBlocks, InvalidBlocks, ExtraIndexEntries, Encodes, Issues |
    Format-Table -AutoSize

Write-Host ''
if ($OnlyProblems) {
    Write-Host ("共分析 {0} 个资源项，输出 {1} 个问题项。" -f $allResults.Count, $orderedResults.Count)
}
else {
    Write-Host ("共分析 {0} 个资源项。" -f $orderedResults.Count)
}
