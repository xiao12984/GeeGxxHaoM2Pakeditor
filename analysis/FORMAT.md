# GEEPAK3 格式恢复记录

## 样本

- `gxx Wzl编辑器.exe`：SHA-256 `8827EDA8BEC75A4C01B8802AFCE39DAAC8D44D1EA7AE57635184755A1DCB6E83`
- `补丁.pak`：SHA-256 `E90A06BC7FF33C745FA5D97D541B404382F814EC0907FFD020C2668D9D0431D6`
- 文件签名：`07 47 45 45 50 41 4B 33`，ASCII 部分为 `GEEPAK3`

## 布局

```text
0x000  8 字节 GEEPAK3 签名
0x008  2 字节保留字段
0x00A  256 字节加密全局头
0x10A  UInt32 索引表
后续   图片块头和载荷
```

解密全局头的已确认字段：

```text
+0x2A UInt32 HeaderSize，固定 266
+0x2E UInt32 SlotCount
+0x32 UInt32 Version，固定 2
+0x36 UInt32 IndexOffset，固定 266
```

索引解密：

```text
offset[i] = encrypted[i] XOR NOT(indexKey[i % 64]) XOR i
```

图片块头固定 16 字节：

```text
+0x00 Byte   图片类型
+0x03 Byte   Alpha 标志
+0x04 UInt16 宽度
+0x06 UInt16 高度
+0x08 Int16  X 偏移
+0x0A Int16  Y 偏移
+0x0C UInt32 zlib 长度，0 表示原始载荷
+0x10        像素载荷
```

## 已确认与未确认

已确认 GEEPAK3 主格式、索引公式、图片块头、zlib 载荷和六种像素布局。默认密码 `QQ1167746` 的三组密钥存在公开交叉实现，可用于读取和写回。

样本使用自定义密码。参考程序把任意密码交给受保护的派生过程生成 256、256、1024 字节密钥；该派生过程尚未恢复为可审计的 C# 算法。因此本项目通过 `PakKeyProfiles.json` 隔离外部精确密钥，避免错误解析或写坏归档。
