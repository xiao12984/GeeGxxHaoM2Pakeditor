using System.Drawing;
using System.Drawing.Drawing2D;
using GeePakEditor.Models;
using GeePakEditor.Services;
using GeePakEditor.Views;

namespace GeePakEditor.Controllers;

/// <summary>
/// 协调主窗口、密码来源和 PAK 归档服务的用户操作流程。
/// </summary>
public sealed class MainController
{
    private readonly IMainView _view;
    private readonly IPakArchiveService _archiveService;
    private readonly PakPasswordService _passwordService;
    private PakArchive? _archive;
    private bool _isDirty;

    /// <summary>
    /// 绑定所有窗口命令，并初始化空状态。
    /// </summary>
    public MainController(IMainView view, IPakArchiveService archiveService, PakPasswordService passwordService)
    {
        _view = view;
        _archiveService = archiveService;
        _passwordService = passwordService;
        _view.OpenRequested += (_, _) => Execute(() => OpenArchive());
        _view.ArchivePathOpenRequested += (_, arguments) => Execute(() => OpenArchive(arguments.FilePath));
        _view.SaveRequested += (_, _) => Execute(SaveArchive);
        _view.SaveAsRequested += (_, _) => Execute(SaveArchiveAs);
        _view.AddRequested += (_, _) => Execute(AddImages);
        _view.ReplaceRequested += (_, _) => Execute(ReplaceImage);
        _view.ExportRequested += (_, _) => Execute(ExportImage);
        _view.DeleteRequested += (_, _) => Execute(DeleteImage);
        _view.SelectionChanged += (_, _) => Execute(RefreshSelection);
        _view.MetadataChanged += (_, _) => MarkMetadataChanged();
        _view.ThumbnailsRequested += (_, arguments) => Execute(() => LoadThumbnails(arguments.Entries));
        _view.ClosingRequested += (_, arguments) => ConfirmClosing(arguments);
        _view.UpdateCommandState(false, false);
    }

    /// <summary>
    /// 从文件对话框或目录树指定的路径打开归档，并统一执行密码确认流程。
    /// </summary>
    /// <param name="requestedPath">目录树指定的归档路径；为空时显示打开文件对话框。</param>
    private void OpenArchive(string? requestedPath = null)
    {
        if (_isDirty && !_view.ConfirmDiscardChanges())
        {
            return;
        }

        var filePath = string.IsNullOrWhiteSpace(requestedPath)
            ? _view.SelectArchiveToOpen()
            : requestedPath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var configuredPassword = _passwordService.ResolvePassword(filePath);
        // 保留 TXT 自动识别能力，但始终让用户确认或修改本次用于打开的密码。
        var password = _view.PromptPassword(filePath, configuredPassword);
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var finalStatus = "未打开归档";
        _view.SetBusy(true, "正在读取并验证 PAK...");
        try
        {
            var opened = _archiveService.Open(filePath, password);
            _archive = opened;
            _isDirty = false;
            // 只有归档验证成功后才更新 TXT，避免将错误密码写入 FilePassword.txt。
            _passwordService.SavePassword(filePath, password);

            _view.BindArchive(opened);
            _view.UpdateCommandState(true, false);
            _view.ShowPreview(null);
            finalStatus = $"已打开 {opened.ImageCount:N0} 张图片";
        }
        finally
        {
            _view.SetBusy(false, finalStatus);
        }
    }

    /// <summary>
    /// 保存到当前文件路径。
    /// </summary>
    private void SaveArchive()
    {
        var archive = RequireArchive();
        SaveToPath(archive, archive.FilePath);
    }

    /// <summary>
    /// 选择新路径后保存完整归档。
    /// </summary>
    private void SaveArchiveAs()
    {
        var archive = RequireArchive();
        var outputPath = _view.SelectArchiveToSave(archive.FilePath);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            SaveToPath(archive, outputPath);
        }
    }

    /// <summary>
    /// 批量导入图片，逐个复用空槽位或追加槽位。
    /// </summary>
    private void AddImages()
    {
        var archive = RequireArchive();
        var imagePaths = _view.SelectImagesToAdd();
        if (imagePaths.Count == 0)
        {
            return;
        }

        _view.SetBusy(true, $"正在导入 {imagePaths.Count} 张图片...");
        PakEntry? lastEntry = null;
        var finalStatus = "导入未完成";
        try
        {
            foreach (var imagePath in imagePaths)
            {
                lastEntry = _archiveService.AddImage(archive, imagePath);
                _isDirty = true;
            }

            _view.RefreshEntries(archive, lastEntry?.Index);
            finalStatus = $"已导入 {imagePaths.Count} 张图片，尚未保存";
        }
        finally
        {
            _view.SetBusy(false, finalStatus);
        }
    }

    /// <summary>
    /// 用外部图片替换当前槽位，并保留原 X/Y 偏移。
    /// </summary>
    private void ReplaceImage()
    {
        var archive = RequireArchive();
        var entry = RequireSelectedEntry();
        var imagePath = _view.SelectReplacementImage();
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        _archiveService.ReplaceImage(archive, entry.Index, imagePath);
        _isDirty = true;
        _view.RefreshEntries(archive, entry.Index);
        RefreshSelection();
        _view.SetStatus($"已替换索引 {entry.Index}，尚未保存");
    }

    /// <summary>
    /// 把当前非空图片解码为 PNG。
    /// </summary>
    private void ExportImage()
    {
        var entry = RequireSelectedEntry();
        var outputPath = _view.SelectImageExportPath(entry.Index);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        _archiveService.ExportImage(entry, outputPath);
        _view.SetStatus($"图片 {entry.Index} 已导出");
    }

    /// <summary>
    /// 清空当前逻辑槽位但不改变后续图片索引。
    /// </summary>
    private void DeleteImage()
    {
        var archive = RequireArchive();
        var entry = RequireSelectedEntry();
        if (!_view.ConfirmDelete(entry.Index))
        {
            return;
        }

        _archiveService.DeleteImage(archive, entry.Index);
        _isDirty = true;
        _view.RefreshEntries(archive, entry.Index);
        RefreshSelection();
        _view.SetStatus($"已清空索引 {entry.Index}，尚未保存");
    }

    /// <summary>
    /// 解码当前图片并同步命令启用状态。
    /// </summary>
    private void RefreshSelection()
    {
        var entry = _view.SelectedEntry;
        var validSelection = _archive is not null && entry is { IsEmpty: false };
        _view.UpdateCommandState(_archive is not null, validSelection);
        if (!validSelection || entry is null)
        {
            _view.ShowPreview(null);
            _view.SetStatus(entry?.IsEmpty == true ? $"索引 {entry.Index} 为空" : "请选择图片");
            return;
        }

        var bitmap = _archiveService.DecodeImage(entry);
        _view.ShowPreview(bitmap);
        _view.SetStatus($"索引 {entry.Index}  |  {entry.Width}x{entry.Height}  |  X={entry.X}, Y={entry.Y}");
    }

    /// <summary>
    /// 属性网格修改 X/Y 后标记当前归档为待保存状态。
    /// </summary>
    private void MarkMetadataChanged()
    {
        var entry = _view.SelectedEntry;
        if (_archive is null || entry is null || entry.IsEmpty)
        {
            return;
        }

        entry.IsModified = true;
        _isDirty = true;
        _view.RefreshEntries(_archive, entry.Index);
        _view.SetStatus($"索引 {entry.Index} 的偏移已修改，尚未保存");
    }

    /// <summary>
    /// 按缩略图网格请求解码当前可见资源，并交由视图缓存缩放后的副本。
    /// </summary>
    /// <param name="entries">需要生成缩略图的非空资源槽位。</param>
    private void LoadThumbnails(IReadOnlyList<PakEntry> entries)
    {
        if (_archive is null || entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries.Where(entry => !entry.IsEmpty).GroupBy(entry => entry.Index).Select(group => group.First()))
        {
            using var image = _archiveService.DecodeImage(entry);
            var thumbnail = CreateThumbnail(image);
            try
            {
                _view.ShowThumbnail(entry.Index, thumbnail);
            }
            catch
            {
                thumbnail.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// 生成不超过 48 像素且不放大小图的独立透明缩略图，避免视图缓存完整资源位图。
    /// </summary>
    /// <param name="image">归档服务解码得到的原始图片。</param>
    /// <returns>由调用方交付给视图并由视图释放的缩略图。</returns>
    private static Image CreateThumbnail(Image image)
    {
        // 参考 Gxx 编辑器的 80 逻辑像素资源网格，将大图限制在约半格尺寸以保留留白。
        const int maximumSize = 48;
        // 小尺寸资源保持原始像素尺寸，仅对超过缩略图上限的图片执行缩小。
        var scale = Math.Min(
            1F,
            Math.Min(maximumSize / (float)image.Width, maximumSize / (float)image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var thumbnail = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(image, new Rectangle(0, 0, width, height));
        return thumbnail;
    }

    /// <summary>
    /// 执行原子保存并刷新列表中的来源偏移与修改状态。
    /// </summary>
    private void SaveToPath(PakArchive archive, string outputPath)
    {
        var finalStatus = "保存未完成";
        _view.SetBusy(true, "正在重建索引并保存 PAK...");
        try
        {
            _archiveService.Save(archive, outputPath);
            _isDirty = false;
            _view.BindArchive(archive);
            finalStatus = $"已保存：{archive.FilePath}";
        }
        finally
        {
            _view.SetBusy(false, finalStatus);
        }
    }

    /// <summary>
    /// 关闭窗口前保护尚未写入 PAK 的编辑状态。
    /// </summary>
    private void ConfirmClosing(FormClosingEventArgs arguments)
    {
        if (_isDirty && !_view.ConfirmDiscardChanges())
        {
            arguments.Cancel = true;
        }
    }

    /// <summary>
    /// 统一把业务异常转为主窗口错误提示。
    /// </summary>
    private void Execute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _view.ShowError(exception.Message);
            _view.SetBusy(false, "操作失败");
        }
    }

    /// <summary>
    /// 返回当前归档；未打开时阻止编辑命令继续执行。
    /// </summary>
    private PakArchive RequireArchive()
    {
        return _archive ?? throw new InvalidOperationException("请先打开一个 GEEPAK3 文件。");
    }

    /// <summary>
    /// 返回当前非空图片槽位。
    /// </summary>
    private PakEntry RequireSelectedEntry()
    {
        var entry = _view.SelectedEntry;
        return entry is { IsEmpty: false }
            ? entry
            : throw new InvalidOperationException("请选择一个非空图片槽位。");
    }
}
