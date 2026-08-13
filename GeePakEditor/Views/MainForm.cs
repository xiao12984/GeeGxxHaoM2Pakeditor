using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// GEE PAK 编辑器主窗口，复刻传统资源编辑器的目录、预览和缩略图浏览布局。
/// </summary>
public sealed class MainForm : XtraForm, IMainView
{
    /// <summary>
    /// 未打开归档时显示的窗口标题。
    /// </summary>
    private const string WindowTitle = "Mir资源管理器";

    /// <summary>
    /// 左侧目录树的固定宽度。
    /// </summary>
    private const int NavigationWidth = 240;

    /// <summary>
    /// 左侧目录树的系统根节点图标键。
    /// </summary>
    private const string ComputerNodeImageKey = "computer";

    /// <summary>
    /// 左侧目录树的库节点图标键。
    /// </summary>
    private const string LibraryNodeImageKey = "library";

    /// <summary>
    /// 左侧目录树的磁盘节点图标键。
    /// </summary>
    private const string DriveNodeImageKey = "drive";

    /// <summary>
    /// 左侧目录树的目录节点图标键。
    /// </summary>
    private const string FolderNodeImageKey = "folder";

    /// <summary>
    /// 左侧目录树的可打开归档文件图标键。
    /// </summary>
    private const string ArchiveNodeImageKey = "archive";

    /// <summary>
    /// 左侧目录树的可执行文件图标键。
    /// </summary>
    private const string ExeNodeImageKey = "exe";

    /// <summary>
    /// 现代主题配色常量。
    /// </summary>
    private static readonly Color AccentColor = Color.FromArgb(0, 122, 204);
    private static readonly Color AccentLightColor = Color.FromArgb(230, 244, 255);
    private static readonly Color PanelBackgroundColor = Color.FromArgb(250, 250, 250);
    private static readonly Color BorderColor = Color.FromArgb(224, 224, 224);
    private static readonly Color StatusBarBackgroundColor = Color.FromArgb(240, 240, 240);

    /// <summary>
    /// 顶部命令区按钮。
    /// </summary>
    private readonly SimpleButton _openButton;
    private readonly SimpleButton _saveButton;
    private readonly SimpleButton _saveAsButton;
    private readonly SimpleButton _addButton;
    private readonly SimpleButton _replaceButton;
    private readonly SimpleButton _exportButton;
    private readonly SimpleButton _deleteButton;

    /// <summary>
    /// 目录树、预览区和缩略图网格。
    /// </summary>
    private readonly ImageList _directoryTreeImages;
    private readonly TreeView _directoryTree;
    private readonly CheckerboardPreviewControl _previewControl;
    private readonly ThumbnailGridControl _thumbnailGrid;

    /// <summary>
    /// 底部状态栏控件与资源偏移编辑器。
    /// </summary>
    private readonly LabelControl _archiveLabel;
    private readonly LabelControl _selectionLabel;
    private readonly LabelControl _statusLabel;
    private readonly MarqueeProgressBarControl _progressBar;
    private readonly SpinEdit _xOffsetEdit;
    private readonly SpinEdit _yOffsetEdit;

    /// <summary>
    /// 主分隔区和右侧预览分隔区，用于在窗口缩放时保持稳定比例。
    /// </summary>
    private readonly SplitContainerControl _workspaceSplit;
    private readonly SplitContainerControl _resourceSplit;

    /// <summary>
    /// 同步选择状态到 X/Y 编辑器期间阻止其再次触发元数据保存事件。
    /// </summary>
    private bool _isSynchronizingMetadata;

    /// <summary>
    /// 当前归档是否允许编辑和写回，用于限制只读 WZL 资源的命令状态。
    /// </summary>
    private bool _canWriteArchive;

    /// <summary>
    /// 当前归档的逻辑槽位总数，用于底部显示当前选择位置。
    /// </summary>
    private int _slotCount;

    /// <summary>
    /// 当前归档的非空图片数量，用于底部显示与原编辑器一致的浏览计数。
    /// </summary>
    private int _imageCount;

    /// <summary>
    /// 创建基于 DevExpress 23.2 的现代化资源编辑器主界面。
    /// </summary>
    public MainForm()
    {
        Text = WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1024, 680);
        Size = new Size(1360, 840);
        Font = new Font("Microsoft YaHei UI", 9F);
        KeyPreview = true;
        Appearance.BackColor = PanelBackgroundColor;

        _openButton = CreateCommandButton("打开", "Open;Size16x16");
        _saveButton = CreateCommandButton("保存", "Save;Size16x16");
        _saveAsButton = CreateCommandButton("另存为", "SaveAs;Size16x16");
        _addButton = CreateCommandButton("导入", "AddItem;Size16x16");
        _replaceButton = CreateCommandButton("替换", "Replace;Size16x16");
        _exportButton = CreateCommandButton("导出", "ExportFile;Size16x16");
        _deleteButton = CreateCommandButton("删除", "Delete;Size16x16");

        _directoryTreeImages = CreateDirectoryTreeImages();
        _directoryTree = CreateDirectoryTree(_directoryTreeImages);
        _previewControl = new CheckerboardPreviewControl { Dock = DockStyle.Fill };
        _thumbnailGrid = new ThumbnailGridControl { Dock = DockStyle.Fill };

        _archiveLabel = new LabelControl
        {
            AutoSizeMode = LabelAutoSizeMode.None,
            Text = "未打开归档",
            Appearance = { ForeColor = Color.FromArgb(100, 100, 100) }
        };
        _selectionLabel = new LabelControl
        {
            AutoSizeMode = LabelAutoSizeMode.None,
            Text = "0/0/0",
            Appearance = { ForeColor = AccentColor, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) }
        };
        _statusLabel = new LabelControl
        {
            AutoSizeMode = LabelAutoSizeMode.None,
            Text = "就绪",
            Appearance = { ForeColor = Color.FromArgb(140, 140, 140) }
        };
        _progressBar = new MarqueeProgressBarControl { Visible = false, Width = 120 };
        _xOffsetEdit = CreateOffsetEditor();
        _yOffsetEdit = CreateOffsetEditor();

        _workspaceSplit = new SplitContainerControl
        {
            Dock = DockStyle.Fill,
            Horizontal = true,
            FixedPanel = SplitFixedPanel.Panel1,
            SplitterPosition = NavigationWidth
        };
        _resourceSplit = new SplitContainerControl
        {
            Dock = DockStyle.Fill,
            Horizontal = false,
            FixedPanel = SplitFixedPanel.Panel1,
            SplitterPosition = 376
        };

        BuildWorkspace();
        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        mainLayout.Controls.Add(CreateHeaderPanel(), 0, 0);
        mainLayout.Controls.Add(_workspaceSplit, 0, 1);
        mainLayout.Controls.Add(CreateStatusPanel(), 0, 2);
        Controls.Add(mainLayout);

        BindEvents();
        PopulateDriveNodes();
        UpdateCommandState(false, false, false);
        SynchronizeMetadataEditors(null);
        Shown += (_, _) => CorrectSplitterDistance();
        Resize += (_, _) => CorrectSplitterDistance();
    }

    /// <inheritdoc />
    public event EventHandler? OpenRequested;

    /// <inheritdoc />
    public event EventHandler<ArchivePathRequestedEventArgs>? ArchivePathOpenRequested;

    /// <inheritdoc />
    public event EventHandler? SaveRequested;

    /// <inheritdoc />
    public event EventHandler? SaveAsRequested;

    /// <inheritdoc />
    public event EventHandler? AddRequested;

    /// <inheritdoc />
    public event EventHandler? ReplaceRequested;

    /// <inheritdoc />
    public event EventHandler? ExportRequested;

    /// <inheritdoc />
    public event EventHandler? DeleteRequested;

    /// <inheritdoc />
    public event EventHandler? SelectionChanged;

    /// <inheritdoc />
    public event EventHandler? MetadataChanged;

    /// <inheritdoc />
    public event EventHandler<ThumbnailRequestEventArgs>? ThumbnailsRequested;

    /// <inheritdoc />
    public event EventHandler<FormClosingEventArgs>? ClosingRequested;

    /// <inheritdoc />
    public PakEntry? SelectedEntry => _thumbnailGrid.SelectedEntry;

    /// <inheritdoc />
    public string? SelectArchiveToOpen()
    {
        using var dialog = new XtraOpenFileDialog
        {
            Title = "打开 GEE PAK",
            Filter = "GEE PAK 文件 (*.pak;*.wzl)|*.pak;*.wzl|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? SelectArchiveToSave(string currentPath)
    {
        using var dialog = new XtraSaveFileDialog
        {
            Title = "另存 GEEPAK3",
            Filter = "GEEPAK3 文件 (*.pak)|*.pak|所有文件 (*.*)|*.*",
            FileName = Path.GetFileName(currentPath),
            InitialDirectory = Path.GetDirectoryName(currentPath),
            AddExtension = true,
            DefaultExt = "pak"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SelectImagesToAdd()
    {
        using var dialog = new XtraOpenFileDialog
        {
            Title = "导入图片",
            Filter = "图片文件 (*.png;*.bmp;*.jpg;*.jpeg;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.gif|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : [];
    }

    /// <inheritdoc />
    public string? SelectReplacementImage()
    {
        using var dialog = new XtraOpenFileDialog
        {
            Title = "选择替换图片",
            Filter = "图片文件 (*.png;*.bmp;*.jpg;*.jpeg;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.gif|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? SelectImageExportPath(int index)
    {
        using var dialog = new XtraSaveFileDialog
        {
            Title = "导出图片",
            Filter = "PNG 图片 (*.png)|*.png",
            FileName = $"{index:D6}.png",
            AddExtension = true,
            DefaultExt = "png"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PromptPassword(string pakPath, string? initialPassword)
    {
        using var dialog = new PasswordDialog(initialPassword);
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.Password
            : null;
    }

    /// <inheritdoc />
    public bool ConfirmDelete(int index)
    {
        return XtraMessageBox.Show(
            this,
            $"确定清空逻辑槽位 {index} 吗？保存后该索引将指向空块。",
            "删除图片",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    /// <inheritdoc />
    public bool ConfirmDiscardChanges()
    {
        return XtraMessageBox.Show(
            this,
            "当前归档存在尚未保存的修改，确定放弃吗？",
            "未保存的修改",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    /// <inheritdoc />
    public void BindArchive(PakArchive archive)
    {
        Text = $"{WindowTitle} - {Path.GetFileName(archive.FilePath)}";
        _archiveLabel.Text = archive.FilePath;
        _canWriteArchive = archive.CanWrite;
        _slotCount = archive.Slots.Count;
        _imageCount = archive.ImageCount;
        RefreshEntries(archive);
    }

    /// <inheritdoc />
    public void RefreshEntries(PakArchive archive, int? selectedIndex = null)
    {
        _slotCount = archive.Slots.Count;
        _imageCount = archive.ImageCount;
        _thumbnailGrid.SetEntries(archive.Slots, selectedIndex);
        SynchronizeMetadataEditors(_thumbnailGrid.SelectedEntry);
        UpdateSelectionLabel();
        if (selectedIndex.HasValue && _thumbnailGrid.SelectedEntry is not null)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void ShowPreview(Image? image)
    {
        var oldImage = _previewControl.Image;
        _previewControl.Image = image;
        oldImage?.Dispose();
        SynchronizeMetadataEditors(image is null ? null : SelectedEntry);
        UpdateSelectionLabel();
    }

    /// <inheritdoc />
    public void ShowThumbnail(int index, Image thumbnail)
    {
        _thumbnailGrid.SetThumbnail(index, thumbnail);
    }

    /// <inheritdoc />
    public void SetBusy(bool busy, string statusText)
    {
        _progressBar.Visible = busy;
        _statusLabel.Text = statusText;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        Enabled = !busy;
    }

    /// <inheritdoc />
    public void SetStatus(string statusText) => _statusLabel.Text = statusText;

    /// <inheritdoc />
    public void UpdateCommandState(bool archiveOpen, bool entrySelected, bool canWriteArchive)
    {
        _canWriteArchive = archiveOpen && canWriteArchive;
        _saveButton.Enabled = _canWriteArchive;
        _saveAsButton.Enabled = _canWriteArchive;
        _addButton.Enabled = _canWriteArchive;
        _replaceButton.Enabled = _canWriteArchive && entrySelected;
        _exportButton.Enabled = archiveOpen && entrySelected;
        _deleteButton.Enabled = _canWriteArchive && entrySelected;
        _xOffsetEdit.Enabled = _canWriteArchive && entrySelected;
        _yOffsetEdit.Enabled = _canWriteArchive && entrySelected;
    }

    /// <inheritdoc />
    public void ShowError(string message)
    {
        XtraMessageBox.Show(this, message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <inheritdoc />
    public void ShowInformation(string message)
    {
        XtraMessageBox.Show(this, message, WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 释放由主窗口托管的完整预览图片。
    /// </summary>
    /// <param name="disposing">是否来自托管资源释放路径。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewControl.Image?.Dispose();
            _previewControl.Image = null;
            _directoryTreeImages.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 创建顶部命令栏容器，移除品牌说明后让工具按钮从左侧开始排列。
    /// </summary>
    /// <returns>顶部命令栏面板。</returns>
    private Control CreateHeaderPanel()
    {
        var header = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.White }
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor, 1F);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        // 顶部区域仅承载命令栏，避免品牌说明占据左侧固定宽度。
        header.Controls.Add(CreateToolbar());
        return header;
    }

    /// <summary>
    /// 创建统一的大图标命令按钮，采用现代扁平圆角风格。
    /// </summary>
    /// <param name="text">按钮显示文字。</param>
    /// <param name="imageUri">DevExpress 内置图标地址。</param>
    /// <returns>已配置的命令按钮。</returns>
    private static SimpleButton CreateCommandButton(string text, string imageUri)
    {
        var button = new SimpleButton
        {
            Text = text,
            Size = new Size(60, 60),
            MinimumSize = new Size(60, 60),
            Margin = new Padding(2, 1, 2, 1),
            ToolTip = text,
            Font = new Font("Microsoft YaHei UI", 7.5F),
            Appearance =
            {
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(60, 60, 60)
            }
        };
        button.ImageOptions.ImageUri.Uri = imageUri;
        button.ImageOptions.Location = ImageLocation.TopCenter;
        // 采用 16px 图标，给中文标题留出更完整的显示空间。
        button.ImageOptions.SvgImageSize = new Size(16, 16);
        // 禁用时保留原始 SVG 颜色，避免灰置后图标与底色混在一起。
        button.ImageOptions.SvgImageColorizationMode = SvgImageColorizationMode.None;
        return button;
    }

    /// <summary>
    /// 创建目录树使用的 Windows 风格小图标集合。
    /// </summary>
    /// <returns>已包含根节点、库、磁盘、目录和归档文件图标的图片列表。</returns>
    private static ImageList CreateDirectoryTreeImages()
    {
        var images = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(18, 18),
            TransparentColor = Color.Transparent
        };
        images.Images.Add(ComputerNodeImageKey, CreateComputerIcon());
        images.Images.Add(LibraryNodeImageKey, CreateFolderIcon(Color.FromArgb(246, 198, 60)));
        images.Images.Add(DriveNodeImageKey, CreateDriveIcon());
        images.Images.Add(FolderNodeImageKey, CreateFolderIcon(Color.FromArgb(246, 198, 60)));
        images.Images.Add(ArchiveNodeImageKey, CreateArchiveIcon());
        images.Images.Add(ExeNodeImageKey, CreateExeIcon());
        return images;
    }

    /// <summary>
    /// 绘制“此电脑”节点的显示器图标。
    /// </summary>
    /// <returns>目录树可直接使用的位图图标。</returns>
    private static Bitmap CreateComputerIcon()
    {
        var bitmap = CreateTreeIconCanvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var screenBrush = new SolidBrush(Color.FromArgb(65, 168, 205));
        using var borderPen = new Pen(Color.FromArgb(70, 92, 104));
        graphics.FillRectangle(screenBrush, 3, 3, 12, 9);
        graphics.DrawRectangle(borderPen, 3, 3, 12, 9);
        graphics.DrawLine(borderPen, 8, 12, 8, 15);
        graphics.DrawLine(borderPen, 5, 15, 12, 15);
        return bitmap;
    }

    /// <summary>
    /// 绘制目录或库节点的文件夹图标。
    /// </summary>
    /// <param name="bodyColor">文件夹主体颜色。</param>
    /// <returns>目录树可直接使用的位图图标。</returns>
    private static Bitmap CreateFolderIcon(Color bodyColor)
    {
        var bitmap = CreateTreeIconCanvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var tabBrush = new SolidBrush(Color.FromArgb(255, 218, 98));
        using var bodyBrush = new SolidBrush(bodyColor);
        using var borderPen = new Pen(Color.FromArgb(192, 140, 35));
        graphics.FillRectangle(tabBrush, 2, 5, 6, 3);
        graphics.FillRectangle(bodyBrush, 2, 7, 14, 8);
        graphics.DrawRectangle(borderPen, 2, 7, 14, 8);
        return bitmap;
    }

    /// <summary>
    /// 绘制本地磁盘节点图标。
    /// </summary>
    /// <returns>目录树可直接使用的位图图标。</returns>
    private static Bitmap CreateDriveIcon()
    {
        var bitmap = CreateTreeIconCanvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var bodyBrush = new SolidBrush(Color.FromArgb(230, 234, 236));
        using var borderPen = new Pen(Color.FromArgb(90, 90, 90));
        using var lightPen = new Pen(Color.FromArgb(250, 250, 250));
        using var ledBrush = new SolidBrush(Color.FromArgb(42, 170, 78));
        var driveBounds = new Rectangle(3, 8, 12, 6);
        graphics.FillRectangle(bodyBrush, driveBounds);
        graphics.DrawRectangle(borderPen, driveBounds);
        graphics.DrawLine(lightPen, 4, 9, 14, 9);
        graphics.FillRectangle(ledBrush, 5, 11, 3, 2);
        return bitmap;
    }

    /// <summary>
    /// 绘制 PAK/WZL 归档文件节点图标。
    /// </summary>
    /// <returns>目录树可直接使用的位图图标。</returns>
    private static Bitmap CreateArchiveIcon()
    {
        var bitmap = CreateTreeIconCanvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pageBrush = new SolidBrush(Color.FromArgb(250, 250, 250));
        using var foldBrush = new SolidBrush(Color.FromArgb(224, 236, 248));
        using var borderPen = new Pen(Color.FromArgb(110, 130, 150));
        using var accentPen = new Pen(AccentColor, 1F);
        var pageBounds = new Rectangle(5, 2, 9, 13);
        graphics.FillRectangle(pageBrush, pageBounds);
        graphics.DrawRectangle(borderPen, pageBounds);
        graphics.FillPolygon(foldBrush, new[] { new Point(10, 2), new Point(14, 6), new Point(10, 6) });
        graphics.DrawLine(borderPen, 10, 2, 14, 6);
        graphics.DrawLine(accentPen, 7, 10, 12, 10);
        graphics.DrawLine(accentPen, 7, 12, 12, 12);
        return bitmap;
    }

    /// <summary>
    /// 绘制可执行文件节点图标。
    /// </summary>
    /// <returns>目录树可直接使用的位图图标。</returns>
    private static Bitmap CreateExeIcon()
    {
        var bitmap = CreateTreeIconCanvas();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var bodyBrush = new SolidBrush(Color.FromArgb(245, 247, 250));
        using var borderPen = new Pen(Color.FromArgb(108, 124, 140));
        using var titleBrush = new SolidBrush(Color.FromArgb(120, 136, 152));
        using var labelPen = new Pen(Color.FromArgb(77, 109, 147));

        var bounds = new Rectangle(4, 2, 10, 14);
        graphics.FillRectangle(bodyBrush, bounds);
        graphics.DrawRectangle(borderPen, bounds);
        graphics.FillRectangle(titleBrush, 4, 2, 10, 3);
        graphics.DrawLine(borderPen, 7, 6, 11, 6);
        graphics.DrawLine(borderPen, 7, 8, 11, 8);
        graphics.DrawLine(labelPen, 7, 11, 10, 11);
        graphics.DrawLine(labelPen, 7, 13, 11, 13);
        return bitmap;
    }

    /// <summary>
    /// 创建目录树图标的透明画布。
    /// </summary>
    /// <returns>18 像素透明位图。</returns>
    private static Bitmap CreateTreeIconCanvas()
    {
        var bitmap = new Bitmap(18, 18);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        return bitmap;
    }

    /// <summary>
    /// 创建左侧磁盘和目录浏览树，采用现代配色。
    /// </summary>
    /// <param name="imageList">目录树节点图标列表。</param>
    /// <returns>用于打开本地 PAK/WZL 文件的目录树。</returns>
    private static TreeView CreateDirectoryTree(ImageList imageList)
    {
        return new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            HotTracking = true,
            ImageList = imageList,
            ImageKey = FolderNodeImageKey,
            SelectedImageKey = FolderNodeImageKey,
            Indent = 24,
            ItemHeight = 28,
            ShowLines = false,
            ShowRootLines = false,
            ShowPlusMinus = true,
            BackColor = Color.FromArgb(207, 207, 207),
            ForeColor = Color.FromArgb(32, 32, 32),
            Font = new Font("Microsoft YaHei UI", 9F)
        };
    }

    /// <summary>
    /// 创建状态栏右侧的整数偏移编辑器。
    /// </summary>
    /// <returns>限制在 Int16 范围内的坐标编辑器。</returns>
    private static SpinEdit CreateOffsetEditor()
    {
        return new SpinEdit
        {
            Size = new Size(48, 22),
            EditValue = 0,
            Enabled = false,
            Properties =
            {
                MinValue = short.MinValue,
                MaxValue = short.MaxValue,
                IsFloatValue = false
            }
        };
    }

    /// <summary>
    /// 组合目录树、棋盘预览区和缩略图网格的现代化主工作区。
    /// </summary>
    private void BuildWorkspace()
    {
        // 左侧目录树面板，带圆角边框感
        var leftPanel = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.FromArgb(207, 207, 207) }
        };
        leftPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, leftPanel.Width - 1, 0, leftPanel.Width - 1, leftPanel.Height);
        };
        leftPanel.Controls.Add(_directoryTree);

        _workspaceSplit.Panel1.MinSize = 180;
        _workspaceSplit.Panel2.MinSize = 520;
        _workspaceSplit.Panel1.Controls.Add(leftPanel);

        // 预览区面板，白色背景带下边框
        var previewPanel = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.White }
        };
        previewPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, previewPanel.Height - 1, previewPanel.Width, previewPanel.Height - 1);
        };
        previewPanel.Controls.Add(_previewControl);

        // 缩略图网格面板，白色背景
        var gridPanel = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.White }
        };
        gridPanel.Controls.Add(_thumbnailGrid);

        _resourceSplit.Panel1.MinSize = 220;
        _resourceSplit.Panel2.MinSize = 180;
        _resourceSplit.Panel1.Controls.Add(previewPanel);
        _resourceSplit.Panel2.Controls.Add(gridPanel);

        var resourceHost = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = PanelBackgroundColor }
        };
        resourceHost.Controls.Add(_resourceSplit);
        _workspaceSplit.Panel2.Controls.Add(resourceHost);
    }

    /// <summary>
    /// 创建顶部大图标命令栏，含分组分隔线。
    /// </summary>
    /// <returns>主窗口顶部工具栏。</returns>
    private Control CreateToolbar()
    {
        var toolbar = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.Transparent }
        };
        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 4, 4, 4)
        };

        // 第一个按钮组从左侧直接开始，移除品牌区后不再保留额外占位。
        buttonFlow.Controls.Add(_openButton);
        buttonFlow.Controls.Add(_saveButton);
        buttonFlow.Controls.Add(_saveAsButton);

        // 添加分隔线和按钮组 2：编辑操作
        buttonFlow.Controls.Add(CreateToolbarSeparator());
        buttonFlow.Controls.Add(_addButton);
        buttonFlow.Controls.Add(_replaceButton);
        buttonFlow.Controls.Add(_exportButton);
        buttonFlow.Controls.Add(_deleteButton);

        toolbar.Controls.Add(buttonFlow);
        return toolbar;
    }

    /// <summary>
    /// 创建工具栏按钮组分隔线。
    /// </summary>
    private static Control CreateToolbarSeparator()
    {
        return new PanelControl
        {
            Size = new Size(2, 48),
            Padding = new Padding(2, 0, 2, 0),
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.Transparent }
        };
    }

    /// <summary>
    /// 创建现代化底部状态栏，包含文件路径、选择位置、状态文字和 X/Y 编辑器。
    /// </summary>
    /// <returns>主窗口底部状态栏。</returns>
    private Control CreateStatusPanel()
    {
        var panel = new PanelControl
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = StatusBarBackgroundColor },
            Padding = new Padding(8, 0, 8, 0)
        };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, 0, panel.Width, 0);
        };

        // 右侧偏移编辑器
        var offsetHost = new PanelControl
        {
            Dock = DockStyle.Right,
            Width = 156,
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.Transparent }
        };
        var xLabel = new LabelControl
        {
            Text = "X",
            Location = new Point(4, 6),
            Appearance = { ForeColor = Color.FromArgb(120, 120, 120), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) }
        };
        _xOffsetEdit.Location = new Point(22, 4);
        _yOffsetEdit.Location = new Point(94, 4);
        var yLabel = new LabelControl
        {
            Text = "Y",
            Location = new Point(76, 6),
            Appearance = { ForeColor = Color.FromArgb(120, 120, 120), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) }
        };
        offsetHost.Controls.AddRange([xLabel, _xOffsetEdit, yLabel, _yOffsetEdit]);

        _archiveLabel.Dock = DockStyle.Left;
        _archiveLabel.Width = 360;
        _archiveLabel.AutoSizeMode = LabelAutoSizeMode.None;
        _archiveLabel.Appearance.TextOptions.VAlignment = VertAlignment.Center;
        _selectionLabel.Dock = DockStyle.Left;
        _selectionLabel.Width = 90;
        _selectionLabel.AutoSizeMode = LabelAutoSizeMode.None;
        _selectionLabel.Appearance.TextOptions.VAlignment = VertAlignment.Center;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Appearance.TextOptions.HAlignment = HorzAlignment.Far;
        _statusLabel.Appearance.TextOptions.VAlignment = VertAlignment.Center;
        _progressBar.Dock = DockStyle.Right;

        panel.Controls.Add(_statusLabel);
        panel.Controls.Add(_progressBar);
        panel.Controls.Add(offsetHost);
        panel.Controls.Add(_selectionLabel);
        panel.Controls.Add(_archiveLabel);
        return panel;
    }

    /// <summary>
    /// 绑定工具栏、目录树、缩略图网格和坐标编辑事件。
    /// </summary>
    private void BindEvents()
    {
        _openButton.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _saveButton.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        _saveAsButton.Click += (_, _) => SaveAsRequested?.Invoke(this, EventArgs.Empty);
        _addButton.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        _replaceButton.Click += (_, _) => ReplaceRequested?.Invoke(this, EventArgs.Empty);
        _exportButton.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        _deleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        _directoryTree.BeforeExpand += OnDirectoryTreeBeforeExpand;
        _directoryTree.NodeMouseDoubleClick += OnDirectoryTreeNodeMouseDoubleClick;
        _thumbnailGrid.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _thumbnailGrid.EntryDoubleClicked += (_, _) =>
        {
            if (_replaceButton.Enabled)
            {
                ReplaceRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _thumbnailGrid.ThumbnailsRequested += (_, arguments) => ThumbnailsRequested?.Invoke(this, arguments);
        _xOffsetEdit.EditValueChanged += (_, _) => UpdateOffsetFromEditor(_xOffsetEdit, true);
        _yOffsetEdit.EditValueChanged += (_, _) => UpdateOffsetFromEditor(_yOffsetEdit, false);
        KeyDown += OnShortcutKeyDown;
        FormClosing += (_, arguments) => ClosingRequested?.Invoke(this, arguments);
    }

    /// <summary>
    /// 将“此电脑”和本机可用盘符加入目录树根节点。
    /// </summary>
    private void PopulateDriveNodes()
    {
        _directoryTree.BeginUpdate();
        try
        {
            _directoryTree.Nodes.Clear();
            var computerNode = new TreeNode("此电脑")
            {
                ImageKey = ComputerNodeImageKey,
                SelectedImageKey = ComputerNodeImageKey
            };
            var libraryNode = new TreeNode("库")
            {
                ImageKey = LibraryNodeImageKey,
                SelectedImageKey = LibraryNodeImageKey
            };

            // 库节点暂不承载业务命令，仅作为资源管理器式导航分组占位。
            _directoryTree.Nodes.Add(computerNode);
            _directoryTree.Nodes.Add(libraryNode);
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                AddDriveNode(_directoryTree.Nodes, drive);
            }
        }
        finally
        {
            _directoryTree.EndUpdate();
        }
    }

    /// <summary>
    /// 首次展开目录时按需读取子目录和可打开的归档文件。
    /// </summary>
    /// <param name="sender">事件来源。</param>
    /// <param name="e">即将展开的目录树节点。</param>
    private void OnDirectoryTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node;
        if (node is null || node.Tag is not string directoryPath || !Directory.Exists(directoryPath))
        {
            return;
        }

        if (node.Nodes.Count == 1 && node.Nodes[0].Tag is null)
        {
            PopulateDirectoryNode(node, directoryPath);
        }
    }

    /// <summary>
    /// 双击归档文件时只将路径交给控制器，复用既有密码和打开流程。
    /// </summary>
    /// <param name="sender">事件来源。</param>
    /// <param name="e">被双击的目录树节点。</param>
    private void OnDirectoryTreeNodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is not string filePath || !File.Exists(filePath) || !IsArchiveFile(filePath))
        {
            return;
        }

        ArchivePathOpenRequested?.Invoke(this, new ArchivePathRequestedEventArgs { FilePath = filePath });
    }

    /// <summary>
    /// 读取一个目录的一层子目录和 PAK/WZL 文件，并容忍无权限目录。
    /// </summary>
    /// <param name="parentNode">需要填充的目录节点。</param>
    /// <param name="directoryPath">待读取的目录完整路径。</param>
    private static void PopulateDirectoryNode(TreeNode parentNode, string directoryPath)
    {
        parentNode.Nodes.Clear();
        try
        {
            foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                AddDirectoryNode(parentNode.Nodes, Path.GetFileName(childDirectory), childDirectory);
            }

            foreach (var archivePath in Directory.EnumerateFiles(directoryPath)
                         .Where(IsArchiveFile)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                parentNode.Nodes.Add(new TreeNode(Path.GetFileName(archivePath))
                {
                    Tag = archivePath,
                    ImageKey = ArchiveNodeImageKey,
                    SelectedImageKey = ArchiveNodeImageKey
                });
            }

            foreach (var executablePath in Directory.EnumerateFiles(directoryPath)
                         .Where(IsExecutableFile)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                parentNode.Nodes.Add(new TreeNode(Path.GetFileName(executablePath))
                {
                    Tag = executablePath,
                    ImageKey = ExeNodeImageKey,
                    SelectedImageKey = ExeNodeImageKey
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 没有权限的目录保持为空节点，避免目录树操作中断。
        }
        catch (IOException)
        {
            // 移动介质或网络路径不可用时忽略当前节点。
        }
    }

    /// <summary>
    /// 向“此电脑”节点加入一个本地磁盘节点。
    /// </summary>
    /// <param name="nodes">目标节点集合。</param>
    /// <param name="drive">本机磁盘信息。</param>
    private static void AddDriveNode(TreeNodeCollection nodes, DriveInfo drive)
    {
        var driveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? $"({driveName}) 本地磁盘"
            : $"({driveName}) {drive.VolumeLabel}";
        AddDirectoryNode(nodes, label, drive.RootDirectory.FullName, DriveNodeImageKey);
    }

    /// <summary>
    /// 向目录树加入延迟读取的目录节点。
    /// </summary>
    /// <param name="nodes">目标节点集合。</param>
    /// <param name="label">界面显示名称。</param>
    /// <param name="directoryPath">目录完整路径。</param>
    /// <param name="imageKey">节点使用的图标键。</param>
    private static void AddDirectoryNode(
        TreeNodeCollection nodes,
        string label,
        string directoryPath,
        string imageKey = FolderNodeImageKey)
    {
        var node = new TreeNode(label)
        {
            Tag = directoryPath,
            ImageKey = imageKey,
            SelectedImageKey = imageKey
        };
        node.Nodes.Add(new TreeNode());
        nodes.Add(node);
    }

    /// <summary>
    /// 判断文件是否为主窗口支持打开的归档格式。
    /// </summary>
    /// <param name="filePath">待判断的文件路径。</param>
    /// <returns>文件扩展名为 PAK 或 WZL 时返回 true。</returns>
    private static bool IsArchiveFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".pak", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".wzl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断文件是否为可执行程序，便于在目录树中展示程序图标。
    /// </summary>
    /// <param name="filePath">待判断的文件路径。</param>
    /// <returns>文件扩展名为 EXE 时返回 true。</returns>
    private static bool IsExecutableFile(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 将当前选择的 X/Y 数值同步到状态栏编辑器。
    /// </summary>
    /// <param name="entry">当前选中资源；为空时禁用编辑器。</param>
    private void SynchronizeMetadataEditors(PakEntry? entry)
    {
        _isSynchronizingMetadata = true;
        try
        {
            var canEdit = _canWriteArchive && entry is { IsEmpty: false };
            _xOffsetEdit.EditValue = canEdit ? entry!.X : 0;
            _yOffsetEdit.EditValue = canEdit ? entry!.Y : 0;
            _xOffsetEdit.Enabled = canEdit;
            _yOffsetEdit.Enabled = canEdit;
        }
        finally
        {
            _isSynchronizingMetadata = false;
        }
    }

    /// <summary>
    /// 将状态栏坐标编辑值写回当前资源，并通知控制器标记待保存。
    /// </summary>
    /// <param name="editor">发生变化的坐标编辑器。</param>
    /// <param name="isX">是否正在修改 X 偏移。</param>
    private void UpdateOffsetFromEditor(SpinEdit editor, bool isX)
    {
        if (_isSynchronizingMetadata || !_canWriteArchive || SelectedEntry is not { IsEmpty: false } entry)
        {
            return;
        }

        var value = Convert.ToInt16(editor.EditValue);
        if ((isX && entry.X == value) || (!isX && entry.Y == value))
        {
            return;
        }

        if (isX)
        {
            entry.X = value;
        }
        else
        {
            entry.Y = value;
        }

        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 更新底部槽位位置文本，保持截图中“当前/总数”的浏览反馈。
    /// </summary>
    private void UpdateSelectionLabel()
    {
        _selectionLabel.Text = SelectedEntry is { } entry
            ? $"{entry.Index}/{_imageCount}/{_slotCount}"
            : $"0/{_imageCount}/{_slotCount}";
    }

    /// <summary>
    /// 支持常见文件操作快捷键和删除快捷键。
    /// </summary>
    /// <param name="sender">事件来源。</param>
    /// <param name="e">按键参数。</param>
    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.S && _saveAsButton.Enabled)
        {
            SaveAsRequested?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.S && _saveButton.Enabled)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete && _deleteButton.Enabled)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// 在窗口缩放后约束左侧导航和上下资源区的最小可用尺寸。
    /// </summary>
    private void CorrectSplitterDistance()
    {
        if (_workspaceSplit.Width >= 760)
        {
            _workspaceSplit.Panel1.MinSize = 180;
            _workspaceSplit.Panel2.MinSize = 520;
            _workspaceSplit.SplitterPosition = Math.Clamp(NavigationWidth, 180, _workspaceSplit.Width - 520);
        }

        if (_resourceSplit.Height >= 470)
        {
            _resourceSplit.Panel1.MinSize = 220;
            _resourceSplit.Panel2.MinSize = 180;
            _resourceSplit.SplitterPosition = Math.Clamp(376, 220, _resourceSplit.Height - 180);
        }
    }
}
