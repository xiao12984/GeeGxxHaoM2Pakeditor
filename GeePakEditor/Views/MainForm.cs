using System.ComponentModel;
using System.Drawing;
using System.IO;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraLayout;
using DevExpress.XtraVerticalGrid;
using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// GEE PAK 编辑器主窗口，提供索引检索、图片预览和编辑命令。
/// </summary>
public sealed class MainForm : XtraForm, IMainView
{
    private readonly SimpleButton _openButton;
    private readonly SimpleButton _saveButton;
    private readonly SimpleButton _saveAsButton;
    private readonly SimpleButton _addButton;
    private readonly SimpleButton _replaceButton;
    private readonly SimpleButton _exportButton;
    private readonly SimpleButton _deleteButton;
    private readonly SearchControl _searchControl;
    private readonly GridControl _gridControl;
    private readonly GridView _gridView;
    private readonly PictureEdit _pictureEdit;
    private readonly PropertyGridControl _propertyGrid;
    private readonly LabelControl _emptyLabel;
    private readonly LabelControl _archiveLabel;
    private readonly LabelControl _statusLabel;
    private readonly MarqueeProgressBarControl _progressBar;
    private readonly SplitContainerControl _mainSplit;
    private BindingList<PakEntry> _entries = [];

    /// <summary>
    /// 创建基于 DevExpress 23.2 控件的主编辑界面。
    /// </summary>
    public MainForm()
    {
        Text = "GEE PAK 编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1024, 680);
        Size = new Size(1360, 840);
        Font = new Font("Microsoft YaHei UI", 9F);
        KeyPreview = true;

        _openButton = CreateCommandButton("打开", "Open;Size16x16");
        _saveButton = CreateCommandButton("保存", "Save;Size16x16");
        _saveAsButton = CreateCommandButton("另存为", "SaveAs;Size16x16");
        _addButton = CreateCommandButton("导入", "AddItem;Size16x16");
        _replaceButton = CreateCommandButton("替换", "Replace;Size16x16");
        _exportButton = CreateCommandButton("导出", "ExportFile;Size16x16");
        _deleteButton = CreateCommandButton("删除", "Delete;Size16x16");
        _searchControl = new SearchControl { Width = 260 };
        _searchControl.Properties.NullValuePrompt = "搜索索引、格式或状态";

        var toolbar = CreateToolbar();
        Controls.Add(toolbar);

        _gridControl = new GridControl { Dock = DockStyle.Fill };
        _gridView = new GridView(_gridControl);
        ConfigureGrid();
        _gridControl.MainView = _gridView;
        _gridControl.ViewCollection.Add(_gridView);
        _searchControl.Client = _gridControl;

        _pictureEdit = new PictureEdit { Dock = DockStyle.Fill };
        _pictureEdit.Properties.SizeMode = PictureSizeMode.Squeeze;
        _pictureEdit.Properties.ShowCameraMenuItem = CameraMenuItemVisibility.Never;
        _pictureEdit.Properties.ShowMenu = false;
        _pictureEdit.BackColor = Color.FromArgb(42, 45, 50);

        _emptyLabel = new LabelControl
        {
            AutoSizeMode = LabelAutoSizeMode.None,
            Dock = DockStyle.Fill,
            Text = "打开 PAK 后选择一个非空图片槽位",
            Appearance = { TextOptions = { HAlignment = HorzAlignment.Center, VAlignment = VertAlignment.Center } }
        };

        var previewHost = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.NoBorder };
        previewHost.Controls.Add(_emptyLabel);
        previewHost.Controls.Add(_pictureEdit);
        _pictureEdit.SendToBack();

        _propertyGrid = new PropertyGridControl { Dock = DockStyle.Fill };
        _propertyGrid.OptionsBehavior.PropertySort = DevExpress.XtraVerticalGrid.PropertySort.Alphabetical;
        _propertyGrid.OptionsView.ShowRootCategories = false;

        _mainSplit = new SplitContainerControl
        {
            Dock = DockStyle.Fill,
            Horizontal = false,
            FixedPanel = SplitFixedPanel.None,
            SplitterPosition = 660
        };
        _mainSplit.Panel1.Controls.Add(CreateListPane());
        _mainSplit.Panel2.Controls.Add(CreateDetailPane(previewHost));
        Controls.Add(_mainSplit);
        _mainSplit.BringToFront();

        _archiveLabel = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Text = "未打开归档" };
        _statusLabel = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Text = "就绪" };
        _progressBar = new MarqueeProgressBarControl { Visible = false, Width = 150 };
        var statusPanel = CreateStatusPanel();
        Controls.Add(statusPanel);
        statusPanel.BringToFront();

        BindEvents();
        UpdateCommandState(false, false);
        ShowPreview(null);
        Shown += (_, _) => CorrectSplitterDistance();
        Resize += (_, _) => CorrectSplitterDistance();
    }

    /// <inheritdoc />
    public event EventHandler? OpenRequested;

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
    public event EventHandler<FormClosingEventArgs>? ClosingRequested;

    /// <inheritdoc />
    public PakEntry? SelectedEntry => _gridView.GetFocusedRow() as PakEntry;

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
    public (string Password, bool Remember)? PromptPassword(string pakPath, string? initialPassword)
    {
        using var dialog = new PasswordDialog(Path.GetFileName(pakPath), initialPassword);
        return dialog.ShowDialog(this) == DialogResult.OK
            ? (dialog.Password, dialog.RememberPassword)
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
        Text = $"GEE PAK 编辑器 - {Path.GetFileName(archive.FilePath)}";
        _archiveLabel.Text = $"{archive.Title}  |  {archive.Slots.Count:N0} 槽  |  {archive.ImageCount:N0} 图片  |  {archive.KeyProfile.Source}";
        RefreshEntries(archive);
    }

    /// <inheritdoc />
    public void RefreshEntries(PakArchive archive, int? selectedIndex = null)
    {
        _archiveLabel.Text = $"{archive.Title}  |  {archive.Slots.Count:N0} 槽  |  {archive.ImageCount:N0} 图片  |  {archive.KeyProfile.Source}";
        _entries = new BindingList<PakEntry>(archive.Slots);
        _gridControl.DataSource = _entries;
        _gridView.RefreshData();
        if (selectedIndex is >= 0 and < int.MaxValue)
        {
            var rowHandle = _gridView.LocateByValue(nameof(PakEntry.Index), selectedIndex.Value);
            if (rowHandle >= 0)
            {
                _gridView.FocusedRowHandle = rowHandle;
            }
        }
    }

    /// <inheritdoc />
    public void ShowPreview(Image? image)
    {
        var oldImage = _pictureEdit.Image;
        _pictureEdit.Image = image;
        oldImage?.Dispose();
        var hasImage = image is not null;
        _pictureEdit.Visible = hasImage;
        _emptyLabel.Visible = !hasImage;
        _propertyGrid.SelectedObject = hasImage ? SelectedEntry : null;
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
    }

    /// <inheritdoc />
    public void ShowError(string message)
    {
        XtraMessageBox.Show(this, message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <inheritdoc />
    public void ShowInformation(string message)
    {
        XtraMessageBox.Show(this, message, "GEE PAK 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 创建带 DevExpress 内置图标的统一命令按钮。
    /// </summary>
    private static SimpleButton CreateCommandButton(string text, string imageUri)
    {
        var button = new SimpleButton { Text = text, Height = 32, MinimumSize = new Size(82, 32) };
        button.ImageOptions.ImageUri.Uri = imageUri;
        return button;
    }

    /// <summary>
    /// 创建顶部命令工具栏和靠右搜索框。
    /// </summary>
    private Control CreateToolbar()
    {
        var toolbar = new PanelControl { Dock = DockStyle.Top, Height = 50, BorderStyle = BorderStyles.NoBorder };
        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 0, 6)
        };
        commands.Controls.AddRange([_openButton, _saveButton, _saveAsButton, _addButton, _replaceButton, _exportButton, _deleteButton]);
        var searchHost = new PanelControl { Dock = DockStyle.Right, Width = 286, BorderStyle = BorderStyles.NoBorder, Padding = new Padding(8) };
        _searchControl.Dock = DockStyle.Fill;
        searchHost.Controls.Add(_searchControl);
        toolbar.Controls.Add(commands);
        toolbar.Controls.Add(searchHost);
        return toolbar;
    }

    /// <summary>
    /// 配置只允许编辑 X/Y 的图片索引表。
    /// </summary>
    private void ConfigureGrid()
    {
        _gridView.OptionsBehavior.Editable = false;
        _gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
        _gridView.OptionsView.ShowGroupPanel = false;
        _gridView.OptionsView.ShowIndicator = false;
        _gridView.OptionsView.ColumnAutoWidth = false;
        _gridView.OptionsFind.AllowFindPanel = false;
        _gridView.FocusRectStyle = DrawFocusRectStyle.RowFocus;
        _gridView.RowHeight = 25;
        _gridView.FocusedRowChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 创建左侧列表区，不额外包裹装饰卡片。
    /// </summary>
    private Control CreateListPane()
    {
        var panel = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.NoBorder, Padding = new Padding(8, 0, 4, 8) };
        panel.Controls.Add(_gridControl);
        return panel;
    }

    /// <summary>
    /// 创建预览与元数据上下分区。
    /// </summary>
    private Control CreateDetailPane(Control previewHost)
    {
        var detailSplit = new SplitContainerControl
        {
            Dock = DockStyle.Fill,
            Horizontal = true,
            FixedPanel = SplitFixedPanel.Panel2,
            SplitterPosition = 480
        };
        detailSplit.Panel2.MinSize = 180;
        detailSplit.Panel1.Controls.Add(previewHost);
        detailSplit.Panel2.Controls.Add(_propertyGrid);
        var panel = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.NoBorder, Padding = new Padding(4, 0, 8, 8) };
        panel.Controls.Add(detailSplit);
        return panel;
    }

    /// <summary>
    /// 创建底部归档摘要、状态文本和忙碌进度条。
    /// </summary>
    private Control CreateStatusPanel()
    {
        var panel = new PanelControl { Dock = DockStyle.Bottom, Height = 32, BorderStyle = BorderStyles.NoBorder, Padding = new Padding(8, 5, 8, 5) };
        _archiveLabel.Dock = DockStyle.Left;
        _archiveLabel.Width = 510;
        _progressBar.Dock = DockStyle.Right;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Appearance.TextOptions.HAlignment = HorzAlignment.Far;
        panel.Controls.Add(_statusLabel);
        panel.Controls.Add(_progressBar);
        panel.Controls.Add(_archiveLabel);
        return panel;
    }

    /// <summary>
    /// 绑定窗口命令与属性网格修改事件。
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
        _propertyGrid.CellValueChanged += (_, _) => MetadataChanged?.Invoke(this, EventArgs.Empty);
        KeyDown += OnShortcutKeyDown;
        FormClosing += (_, arguments) => ClosingRequested?.Invoke(this, arguments);
    }

    /// <summary>
    /// 支持常见文件操作快捷键，不在界面中占用额外说明文字。
    /// </summary>
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
    /// 保持列表和详情两侧都具有稳定可读的最小宽度。
    /// </summary>
    private void CorrectSplitterDistance()
    {
        if (_mainSplit.Width < 700)
        {
            return;
        }

        _mainSplit.Panel1.MinSize = 0;
        _mainSplit.Panel2.MinSize = 0;
        var preferred = Math.Clamp((int)(_mainSplit.Width * 0.56), 500, _mainSplit.Width - 390);
        _mainSplit.SplitterPosition = preferred;
        _mainSplit.Panel1.MinSize = 500;
        _mainSplit.Panel2.MinSize = 380;
    }
}
