using System.Drawing;
using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// 主窗口向控制器暴露的用户操作与显示契约。
/// </summary>
public interface IMainView
{
    /// <summary>用户请求打开归档。</summary>
    event EventHandler? OpenRequested;

    /// <summary>用户请求新建归档。</summary>
    event EventHandler? NewRequested;

    /// <summary>用户从目录树指定归档文件请求打开。</summary>
    event EventHandler<ArchivePathRequestedEventArgs>? ArchivePathOpenRequested;

    /// <summary>用户请求加载资源文件夹。</summary>
    event EventHandler? FolderOpenRequested;

    /// <summary>用户请求保存当前归档。</summary>
    event EventHandler? SaveRequested;

    /// <summary>用户请求另存当前归档。</summary>
    event EventHandler? SaveAsRequested;

    /// <summary>用户请求导入一个或多个图片。</summary>
    event EventHandler? AddRequested;

    /// <summary>用户请求替换当前图片。</summary>
    event EventHandler? ReplaceRequested;

    /// <summary>用户请求导出当前图片。</summary>
    event EventHandler? ExportRequested;

    /// <summary>用户请求删除当前图片。</summary>
    event EventHandler? DeleteRequested;

    /// <summary>当前列表选择发生变化。</summary>
    event EventHandler? SelectionChanged;

    /// <summary>当前图片的 X/Y 元数据已修改。</summary>
    event EventHandler? MetadataChanged;

    /// <summary>缩略图网格请求加载当前可见资源的缩略图。</summary>
    event EventHandler<ThumbnailRequestEventArgs>? ThumbnailsRequested;

    /// <summary>主窗口即将关闭。</summary>
    event EventHandler<FormClosingEventArgs>? ClosingRequested;

    /// <summary>当前选中的图片槽位。</summary>
    PakEntry? SelectedEntry { get; }

    /// <summary>显示打开 PAK 对话框。</summary>
    string? SelectArchiveToOpen();

    /// <summary>显示资源文件夹选择对话框。</summary>
    string? SelectFolderToOpen();

    /// <summary>显示新建归档设置对话框。</summary>
    NewArchiveSettings? PromptNewArchiveSettings();

    /// <summary>选择新建归档的保存路径。</summary>
    string? SelectArchiveToCreate(NewArchiveSettings settings);

    /// <summary>按当前归档格式显示另存为对话框。</summary>
    string? SelectArchiveToSave(PakArchive archive);

    /// <summary>选择一个或多个待导入图片。</summary>
    IReadOnlyList<string> SelectImagesToAdd();

    /// <summary>选择用于替换当前槽位的图片。</summary>
    string? SelectReplacementImage();

    /// <summary>选择当前图片的 PNG 导出路径。</summary>
    string? SelectImageExportPath(int index);

    /// <summary>显示密码输入框，并返回用户确认后的密码。</summary>
    string? PromptPassword(string pakPath, string? initialPassword);

    /// <summary>确认删除当前逻辑槽位。</summary>
    bool ConfirmDelete(int index);

    /// <summary>确认放弃尚未保存的修改。</summary>
    bool ConfirmDiscardChanges();

    /// <summary>绑定完整归档并更新窗口标题。</summary>
    void BindArchive(PakArchive archive);

    /// <summary>绑定用户选择的资源文件夹分类。</summary>
    void BindFolder(ResourceFolderCatalog catalog);

    /// <summary>刷新列表并尽量恢复指定索引的选择。</summary>
    void RefreshEntries(PakArchive archive, int? selectedIndex = null);

    /// <summary>更新图片预览；窗口负责释放旧图片。</summary>
    void ShowPreview(Image? image);

    /// <summary>向缩略图网格交付单个图片；窗口接管该图片的释放责任。</summary>
    /// <param name="entry">生成该缩略图时对应的资源对象，用于丢弃过期任务结果。</param>
    void ShowThumbnail(int index, PakEntry entry, Image thumbnail);

    /// <summary>把界面更新切回主窗口线程执行。</summary>
    void InvokeOnUi(Action action);

    /// <summary>清除指定资源对象的失败请求标记，使其重新进入视口时可以重试。</summary>
    /// <param name="index">资源逻辑索引。</param>
    /// <param name="entry">发起请求时对应的资源对象，用于忽略过期任务。</param>
    bool ResetThumbnailRequest(int index, PakEntry entry);

    /// <summary>设置忙碌状态与底部状态文本。</summary>
    void SetBusy(bool busy, string statusText);

    /// <summary>更新底部状态文本。</summary>
    void SetStatus(string statusText);

    /// <summary>根据归档、选择和写回能力启用或禁用命令。</summary>
    void UpdateCommandState(bool archiveOpen, bool entrySelected, bool canWriteArchive);

    /// <summary>显示错误消息。</summary>
    void ShowError(string message);

    /// <summary>显示普通提示消息。</summary>
    void ShowInformation(string message);
}
