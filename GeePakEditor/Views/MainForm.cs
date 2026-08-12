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
    private const string WindowTitle = "Gxx 资源编辑器 (Wil, Wis, Wzl, Pak)";

    /// <summary>
    /// 左侧目录树的固定宽度。
    /// </summary>
    private const int NavigationWidth = 240;

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
    /// 资源筛选框、目录树、预览区和缩略图网格。
    /// </summary>
    private readonly TextEdit _filterEdit;
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

        _openButton = CreateCommandButton("打开", "Open;Size32x32");
        _saveButton = CreateCommandButton("保存", "Save;Size32x32");
        _saveAsButton = CreateCommandButton("另存为", "SaveAs;Size32x32");
        _addButton = CreateCommandButton("导入", "AddItem;Size32x32");
        _replaceButton = CreateCommandButton("替换", "Replace;Size32x32");
        _exportButton = CreateCommandButton("导出", "ExportFile;Size32x32");
        _deleteButton = CreateCommandButton("删除", "Delete;Size32x32");

        _filterEdit = new TextEdit { Width = 220 };
        _filterEdit.Properties.NullValuePrompt = "筛选索引、格式或状态";
        _filterEdit.Properties.ShowNullValuePromptWhenFocused = true;
        _filterEdit.Properties.Appearance.Font = new Font("Microsoft YaHei UI", 9F);

        _directoryTree = CreateDirectoryTree();
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        mainLayout.Controls.Add(CreateHeaderPanel(), 0, 0);
        mainLayout.Controls.Add(_workspaceSplit, 0, 1);
        mainLayout.Controls.Add(CreateStatusPanel(), 0, 2);
        Controls.Add(mainLayout);

        BindEvents();
        PopulateDriveNodes();
        UpdateCommandState(false, false);
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
            Title = "另存 GEE PAK",
            Filter = "GEE PAK 文件 (*.pak)|*.pak|WZL 文件 (*.wzl)|*.wzl|所有文件 (*.*)|*.*",
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
    public void UpdateCommandState(bool archiveOpen, bool entrySelected)
    {
        _saveButton.Enabled = archiveOpen;
        _saveAsButton.Enabled = archiveOpen;
        _addButton.Enabled = archiveOpen;
        _replaceButton.Enabled = archiveOpen && entrySelected;
        _exportButton.Enabled = archiveOpen && entrySelected;
        _deleteButton.Enabled = archiveOpen && entrySelected;
        _xOffsetEdit.Enabled = archiveOpen && entrySelected;
        _yOffsetEdit.Enabled = archiveOpen && entrySelected;
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
            Size = new Size(68, 72),
            MinimumSize = new Size(68, 72),
            ToolTip = text,
            Font = new Font("Microsoft YaHei UI", 8F),
            Appearance =
            {
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(60, 60, 60)
            }
        };
        button.ImageOptions.ImageUri.Uri = imageUri;
        button.ImageOptions.Location = ImageLocation.TopCenter;
        button.ImageOptions.SvgImageSize = new Size(24, 24);
        return button;
    }

    /// <summary>
    /// 创建左侧磁盘和目录浏览树，采用现代配色。
    /// </summary>
    /// <returns>用于打开本地 PAK/WZL 文件的目录树。</returns>
    private static TreeView CreateDirectoryTree()
    {
        return new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            HotTracking = true,
            ShowLines = false,
            ShowRootLines = false,
            ShowPlusMinus = true,
            BackColor = Color.FromArgb(245, 245, 245),
            ForeColor = Color.FromArgb(50, 50, 50),
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
            Appearance = { BackColor = Color.FromArgb(245, 245, 245) }
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
    /// 创建顶部大图标命令栏，含分组分隔线和资源筛选框。
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
            Padding = new Padding(0, 8, 8, 8)
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

        // 添加分隔线和筛选框
        buttonFlow.Controls.Add(CreateToolbarSeparator());
        var filterWrapper = new PanelControl
        {
            Size = new Size(200, 72),
            BorderStyle = BorderStyles.NoBorder,
            Appearance = { BackColor = Color.Transparent },
            Padding = new Padding(0, 20, 0, 20)
        };
        _filterEdit.Dock = DockStyle.Fill;
        filterWrapper.Controls.Add(_filterEdit);
        buttonFlow.Controls.Add(filterWrapper);

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
            Size = new Size(2, 72),
            Padding = new Padding(4, 0, 4, 0),
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
    /// 绑定工具栏、目录树、缩略图网格、筛选和坐标编辑事件。
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
        _filterEdit.EditValueChanged += (_, _) => _thumbnailGrid.SetFilterText(_filterEdit.Text);
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
    /// 将本机可用盘符加入目录树根节点。
    /// </summary>
    private void PopulateDriveNodes()
    {
        _directoryTree.BeginUpdate();
        try
        {
            _directoryTree.Nodes.Clear();
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.Name}"
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})";
                AddDirectoryNode(_directoryTree.Nodes, label, drive.RootDirectory.FullName);
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
                parentNode.Nodes.Add(new TreeNode(Path.GetFileName(archivePath)) { Tag = archivePath });
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
    /// 向目录树加入延迟读取的目录节点。
    /// </summary>
    /// <param name="nodes">目标节点集合。</param>
    /// <param name="label">界面显示名称。</param>
    /// <param name="directoryPath">目录完整路径。</param>
    private static void AddDirectoryNode(TreeNodeCollection nodes, string label, string directoryPath)
    {
        var node = new TreeNode(label) { Tag = directoryPath };
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
    /// 将当前选择的 X/Y 数值同步到状态栏编辑器。
    /// </summary>
    /// <param name="entry">当前选中资源；为空时禁用编辑器。</param>
    private void SynchronizeMetadataEditors(PakEntry? entry)
    {
        _isSynchronizingMetadata = true;
        try
        {
            var canEdit = entry is { IsEmpty: false };
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
        if (_isSynchronizingMetadata || SelectedEntry is not { IsEmpty: false } entry)
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
        else if (e.Control && e.Shift && e.KeyCode == Keys.S)
        {
            SaveAsRequested?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
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
