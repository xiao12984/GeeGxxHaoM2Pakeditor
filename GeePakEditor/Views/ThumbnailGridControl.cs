using System.Drawing.Drawing2D;
using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// 使用固定单元格虚拟化显示 PAK 资源缩略图的现代化滚动网格。
/// </summary>
internal sealed class ThumbnailGridControl : ScrollableControl
{
    /// <summary>
    /// 每个资源格的固定宽度，参考 LibraryEditor 的紧凑预览节奏收窄。
    /// </summary>
    private const int CellWidth = 80;

    /// <summary>
    /// 每个资源格的固定高度，仅保留缩略图与索引，避免格式文字遮挡预览。
    /// </summary>
    private const int CellHeight = 84;

    /// <summary>
    /// 单元格之间的间距，压缩为更接近 LibraryEditor 的密度。
    /// </summary>
    private const int CellMargin = 2;

    /// <summary>
    /// 资源格内缩略图的边距，避免小格子里图像贴边。
    /// </summary>
    private const int ThumbnailPadding = 4;

    /// <summary>
    /// 单元格圆角半径，随格子尺寸同步收紧。
    /// </summary>
    private const int CornerRadius = 4;

    /// <summary>
    /// 现代主题配色。
    /// </summary>
    private static readonly Color CellBackgroundColor = Color.FromArgb(248, 248, 248);
    private static readonly Color SelectedCellColor = Color.FromArgb(0, 122, 204);
    private static readonly Color SelectedCellLightColor = Color.FromArgb(227, 242, 253);
    private static readonly Color SelectedBorderColor = Color.FromArgb(0, 105, 180);
    private static readonly Color CellBorderColor = Color.FromArgb(224, 224, 224);
    private static readonly Color HoverCellColor = Color.FromArgb(235, 245, 255);
    private static readonly Color TextColor = Color.FromArgb(60, 60, 60);
    private static readonly Color SelectedTextColor = Color.White;
    private static readonly Color EmptyTextColor = Color.FromArgb(160, 160, 160);
    private static readonly Color ShadowColor = Color.FromArgb(30, 0, 0, 0);

    /// <summary>
    /// 预缓存的 GDI 画刷，避免每帧绘制重复创建。
    /// </summary>
    private static readonly SolidBrush CellBackgroundBrush = new(CellBackgroundColor);
    private static readonly SolidBrush SelectedCellBrush = new(SelectedCellColor);
    private static readonly SolidBrush HoverCellBrush = new(HoverCellColor);
    private static readonly SolidBrush ShadowBrush = new(ShadowColor);
    private static readonly SolidBrush ThumbBgBrush = new(Color.White);

    /// <summary>
    /// 预缓存的 GDI 画笔。
    /// </summary>
    private static readonly Pen SelectedBorderPen = new(SelectedBorderColor, 1.5F);
    private static readonly Pen CellBorderPenValue = new(CellBorderColor, 1F);

    /// <summary>
    /// 预缓存的字体。
    /// </summary>
    private static readonly Font LoadingFont = new("Microsoft YaHei UI", 6F);

    /// <summary>
    /// 当前完整资源槽位集合，保留实际对象供控制器读取。
    /// </summary>
    private readonly List<PakEntry> _allEntries = [];

    /// <summary>
    /// 当前筛选后参与绘制和导航的资源槽位。
    /// </summary>
    private readonly List<PakEntry> _visibleEntries = [];

    /// <summary>
    /// 已经缩放完成并由本控件负责释放的缩略图缓存。
    /// </summary>
    private readonly Dictionary<int, Image> _thumbnailCache = [];

    /// <summary>
    /// 已请求但尚未交付图片的索引，用于防止滚动时重复解码。
    /// </summary>
    private readonly HashSet<int> _requestedIndexes = [];

    /// <summary>
    /// 当前选中资源在筛选集合中的位置，负数表示未选择。
    /// </summary>
    private int _selectedVisibleIndex = -1;

    /// <summary>
    /// 当前鼠标悬停的资源位置，负数表示无悬停。
    /// </summary>
    private int _hoveredVisibleIndex = -1;

    /// <summary>
    /// 当前按列排列的资源格数量。
    /// </summary>
    private int _columnCount = 1;

    /// <summary>
    /// 当前资源筛选文本，用于列表刷新后保持用户正在使用的筛选条件。
    /// </summary>
    private string _filterText = string.Empty;

    /// <summary>
    /// 初始化支持键盘导航、滚动和无闪烁绘制的现代化资源网格。
    /// </summary>
    public ThumbnailGridControl()
    {
        // 资源网格完全自绘，统一开启双缓冲和尺寸变化重绘，避免滚动后残留旧编号。
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        AutoScroll = true;
        BackColor = Color.White;
        DoubleBuffered = true;
        TabStop = true;
        // 索引文字保持清晰，但整体字号略收紧以适配更小的资源格。
        Font = new Font("Microsoft YaHei UI", 7.5F);
    }

    /// <summary>
    /// 当前资源选择发生变化时通知主窗口更新预览和命令状态。
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// 用户双击资源格时通知主窗口执行替换操作。
    /// </summary>
    public event EventHandler? EntryDoubleClicked;

    /// <summary>
    /// 当前可见区域出现未缓存资源时请求控制器批量生成缩略图。
    /// </summary>
    public event EventHandler<ThumbnailRequestEventArgs>? ThumbnailsRequested;

    /// <summary>
    /// 获取当前选中的实际资源槽位。
    /// </summary>
    public PakEntry? SelectedEntry => _selectedVisibleIndex is >= 0 && _selectedVisibleIndex < _visibleEntries.Count
        ? _visibleEntries[_selectedVisibleIndex]
        : null;

    /// <summary>
    /// 设置全部资源槽位并清理属于上一份归档的缩略图缓存。
    /// </summary>
    /// <param name="entries">按逻辑索引排列的完整资源槽位。</param>
    /// <param name="selectedIndex">需要恢复的逻辑索引；未提供时清除选择。</param>
    public void SetEntries(IReadOnlyList<PakEntry> entries, int? selectedIndex)
    {
        DisposeThumbnailCache();
        _allEntries.Clear();
        _allEntries.AddRange(entries);
        _requestedIndexes.Clear();
        ApplyFilterInternal(_filterText, selectedIndex, false);
    }

    /// <summary>
    /// 按资源索引、状态或像素格式筛选缩略图网格。
    /// </summary>
    /// <param name="filterText">用户输入的筛选文本。</param>
    public void SetFilterText(string? filterText)
    {
        var selectedIndex = SelectedEntry?.Index;
        _filterText = filterText ?? string.Empty;
        ApplyFilterInternal(_filterText, selectedIndex, true);
    }

    /// <summary>
    /// 接收控制器交付的缩略图并刷新对应资源格。
    /// </summary>
    /// <param name="index">资源逻辑索引。</param>
    /// <param name="thumbnail">由视图缓存和释放的缩略图。</param>
    public void SetThumbnail(int index, Image thumbnail)
    {
        if (!_allEntries.Any(entry => entry.Index == index))
        {
            thumbnail.Dispose();
            return;
        }

        if (_thumbnailCache.Remove(index, out var oldThumbnail))
        {
            oldThumbnail.Dispose();
        }

        _thumbnailCache[index] = thumbnail;
        _requestedIndexes.Remove(index);
        var visibleIndex = _visibleEntries.FindIndex(entry => entry.Index == index);
        if (visibleIndex >= 0)
        {
            InvalidateCell(visibleIndex);
        }
    }

    /// <summary>
    /// 在尺寸变化后重新计算网格列数和可滚动范围。
    /// </summary>
    /// <param name="e">尺寸变化参数。</param>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateLayoutMetrics();
        RequestVisibleThumbnails();
        Invalidate(ClientRectangle);
    }

    /// <summary>
    /// 在滚动后请求新出现的缩略图并重绘可见区域。
    /// </summary>
    /// <param name="se">滚动参数。</param>
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        RequestVisibleThumbnails();
        Invalidate();
    }

    /// <summary>
    /// 绘制圆角资源单元格、缩略图、选中状态、悬停效果和索引。
    /// </summary>
    /// <param name="e">控件绘制上下文。</param>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_visibleEntries.Count == 0)
        {
            DrawEmptyState(e.Graphics);
            return;
        }

        var (firstIndex, lastIndex) = GetVisibleRange();
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var cellBounds = GetCellClientRectangle(index);
            if (cellBounds.IntersectsWith(e.ClipRectangle))
            {
                DrawCell(e.Graphics, index, cellBounds);
            }
        }
    }

    /// <summary>
    /// 根据鼠标位置更新当前选择和悬停状态。
    /// </summary>
    /// <param name="e">鼠标参数。</param>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var entryIndex = HitTest(e.Location);
        if (entryIndex >= 0)
        {
            SetSelectedVisibleIndex(entryIndex, true);
        }
    }

    /// <summary>
    /// 跟踪鼠标移动以更新悬停效果。
    /// </summary>
    /// <param name="e">鼠标参数。</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var entryIndex = HitTest(e.Location);
        if (entryIndex != _hoveredVisibleIndex)
        {
            var previousHover = _hoveredVisibleIndex;
            _hoveredVisibleIndex = entryIndex;
            if (previousHover >= 0 && previousHover < _visibleEntries.Count)
            {
                InvalidateCell(previousHover);
            }
            if (_hoveredVisibleIndex >= 0 && _hoveredVisibleIndex < _visibleEntries.Count)
            {
                InvalidateCell(_hoveredVisibleIndex);
            }
        }
    }

    /// <summary>
    /// 鼠标离开时清除悬停状态。
    /// </summary>
    /// <param name="e">事件参数。</param>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredVisibleIndex >= 0)
        {
            var previousHover = _hoveredVisibleIndex;
            _hoveredVisibleIndex = -1;
            InvalidateCell(previousHover);
        }
    }

    /// <summary>
    /// 在双击资源格时触发既定的替换图片操作。
    /// </summary>
    /// <param name="e">鼠标参数。</param>
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTest(e.Location) >= 0)
        {
            EntryDoubleClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 支持方向键、翻页键和首尾键进行资源网格导航。
    /// </summary>
    /// <param name="e">键盘参数。</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_visibleEntries.Count == 0)
        {
            return;
        }

        var currentIndex = Math.Max(0, _selectedVisibleIndex);
        var targetIndex = e.KeyCode switch
        {
            Keys.Left => currentIndex - 1,
            Keys.Right => currentIndex + 1,
            Keys.Up => currentIndex - _columnCount,
            Keys.Down => currentIndex + _columnCount,
            Keys.PageUp => currentIndex - (_columnCount * Math.Max(1, ClientSize.Height / CellHeight)),
            Keys.PageDown => currentIndex + (_columnCount * Math.Max(1, ClientSize.Height / CellHeight)),
            Keys.Home => 0,
            Keys.End => _visibleEntries.Count - 1,
            _ => -1
        };

        if (targetIndex < 0 && e.KeyCode is not Keys.Left and not Keys.Up and not Keys.PageUp)
        {
            return;
        }

        if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End)
        {
            targetIndex = Math.Clamp(targetIndex, 0, _visibleEntries.Count - 1);
            SetSelectedVisibleIndex(targetIndex, true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// 释放缓存缩略图，避免切换归档后保留 GDI 图像对象。
    /// </summary>
    /// <param name="disposing">是否来自托管资源释放路径。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeThumbnailCache();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 根据当前筛选文本重新构建可见集合，并恢复可见的原选择。
    /// </summary>
    /// <param name="filterText">筛选文本。</param>
    /// <param name="selectedIndex">需要恢复的逻辑索引。</param>
    /// <param name="raiseSelectionChanged">是否在选择变化时通知外层。</param>
    private void ApplyFilterInternal(string filterText, int? selectedIndex, bool raiseSelectionChanged)
    {
        var previousEntryIndex = SelectedEntry?.Index;
        var normalizedFilter = filterText.Trim();
        _visibleEntries.Clear();
        _visibleEntries.AddRange(
            _allEntries.Where(entry =>
                normalizedFilter.Length == 0 ||
                entry.Index.ToString("D5").Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                entry.StateText.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                entry.FormatText.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)));

        _selectedVisibleIndex = selectedIndex.HasValue
            ? _visibleEntries.FindIndex(entry => entry.Index == selectedIndex.Value)
            : -1;
        _hoveredVisibleIndex = -1;
        UpdateLayoutMetrics();
        AutoScrollPosition = Point.Empty;
        Invalidate();
        RequestVisibleThumbnails();

        if (raiseSelectionChanged && previousEntryIndex != SelectedEntry?.Index)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 计算当前控件宽度对应的列数和虚拟滚动区域。
    /// </summary>
    private void UpdateLayoutMetrics()
    {
        _columnCount = Math.Max(1, ClientSize.Width / (CellWidth + CellMargin));
        var rowCount = (int)Math.Ceiling(_visibleEntries.Count / (double)_columnCount);
        AutoScrollMinSize = new Size(
            Math.Max(ClientSize.Width, _columnCount * (CellWidth + CellMargin)),
            Math.Max(ClientSize.Height, rowCount * (CellHeight + CellMargin)));
    }

    /// <summary>
    /// 计算当前视口与前后一个缓冲行所覆盖的资源范围。
    /// </summary>
    /// <returns>首尾可见资源在筛选集合中的位置。</returns>
    private (int FirstIndex, int LastIndex) GetVisibleRange()
    {
        if (_visibleEntries.Count == 0)
        {
            return (0, -1);
        }

        var scrollY = -AutoScrollPosition.Y;
        var firstRow = Math.Max(0, (scrollY / (CellHeight + CellMargin)) - 1);
        var lastRow = Math.Min(
            (int)Math.Ceiling(_visibleEntries.Count / (double)_columnCount) - 1,
            ((scrollY + ClientSize.Height) / (CellHeight + CellMargin)) + 1);
        return (
            firstRow * _columnCount,
            Math.Min(_visibleEntries.Count - 1, ((lastRow + 1) * _columnCount) - 1));
    }

    /// <summary>
    /// 请求当前视口中未缓存且尚未请求的非空资源缩略图。
    /// </summary>
    private void RequestVisibleThumbnails()
    {
        if (!IsHandleCreated || _visibleEntries.Count == 0)
        {
            return;
        }

        var (firstIndex, lastIndex) = GetVisibleRange();
        if (lastIndex < firstIndex)
        {
            return;
        }

        var requestedEntries = new List<PakEntry>();
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var entry = _visibleEntries[index];
            if (entry.IsEmpty || _thumbnailCache.ContainsKey(entry.Index) || !_requestedIndexes.Add(entry.Index))
            {
                continue;
            }

            requestedEntries.Add(entry);
        }

        if (requestedEntries.Count > 0)
        {
            ThumbnailsRequested?.Invoke(this, new ThumbnailRequestEventArgs { Entries = requestedEntries });
        }
    }

    /// <summary>
    /// 绘制单个圆角资源格及其缩略图和索引标签。
    /// </summary>
    /// <param name="graphics">控件绘制上下文。</param>
    /// <param name="visibleIndex">资源在筛选集合中的位置。</param>
    /// <param name="cellBounds">资源格的虚拟绘制区域。</param>
    private void DrawCell(Graphics graphics, int visibleIndex, Rectangle cellBounds)
    {
        var entry = _visibleEntries[visibleIndex];
        var selected = visibleIndex == _selectedVisibleIndex;
        var hovered = visibleIndex == _hoveredVisibleIndex && !selected;

        // 定义绘制区域（减去间距）
        var drawBounds = new Rectangle(
            cellBounds.Left + CellMargin / 2,
            cellBounds.Top + CellMargin / 2,
            cellBounds.Width - CellMargin,
            cellBounds.Height - CellMargin);

        using var path = GetRoundedRectangle(drawBounds, CornerRadius);

        // 绘制阴影（仅选中时）
        if (selected)
        {
            var shadowBounds = new Rectangle(drawBounds.X + 1, drawBounds.Y + 2, drawBounds.Width, drawBounds.Height);
            using var shadowPath = GetRoundedRectangle(shadowBounds, CornerRadius);
            graphics.FillPath(ShadowBrush, shadowPath);
        }

        // 绘制单元格背景
        var bgBrush = selected ? SelectedCellBrush : (hovered ? HoverCellBrush : CellBackgroundBrush);
        graphics.FillPath(bgBrush, path);

        // 绘制单元格边框
        var borderPen = selected ? SelectedBorderPen : CellBorderPenValue;
        graphics.DrawPath(borderPen, path);

        // 绘制缩略图区域，按更紧凑的格子重新分配可视面积。
        var thumbnailArea = new Rectangle(
            drawBounds.Left + ThumbnailPadding,
            drawBounds.Top + ThumbnailPadding,
            drawBounds.Width - (ThumbnailPadding * 2),
            drawBounds.Height - 24);

        // 缩略图背景
        using var thumbPath = GetRoundedRectangle(thumbnailArea, 3);
        graphics.FillPath(ThumbBgBrush, thumbPath);

        if (_thumbnailCache.TryGetValue(entry.Index, out var thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var imageBounds = GetImageBounds(thumbnail.Size, thumbnailArea);
            // 裁剪到缩略图区域
            var originalClip = graphics.Clip;
            graphics.SetClip(thumbPath);
            graphics.DrawImage(thumbnail, imageBounds);
            graphics.Clip = originalClip;
        }
        else if (!entry.IsEmpty)
        {
            // 显示加载中的占位符
            var loadingText = "...";
            var loadingSize = TextRenderer.MeasureText(graphics, loadingText, LoadingFont);
            TextRenderer.DrawText(
                graphics,
                loadingText,
                LoadingFont,
                new Point(
                    thumbnailArea.Left + (thumbnailArea.Width - loadingSize.Width) / 2,
                    thumbnailArea.Top + (thumbnailArea.Height - loadingSize.Height) / 2),
                EmptyTextColor);
        }

        // 绘制索引标签
        var textBounds = new Rectangle(
            drawBounds.Left + 3,
            drawBounds.Bottom - 16,
            drawBounds.Width - 6,
            14);
        var textColor = selected ? SelectedTextColor : TextColor;
        TextRenderer.DrawText(
            graphics,
            entry.Index.ToString("D5"),
            Font,
            textBounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

    }

    /// <summary>
    /// 绘制空状态提示。
    /// </summary>
    /// <param name="graphics">控件绘制上下文。</param>
    private void DrawEmptyState(Graphics graphics)
    {
        if (_allEntries.Count == 0)
        {
            var message = "打开 PAK 或 WZL 归档以浏览资源";
            var messageSize = TextRenderer.MeasureText(graphics, message, Font);
            TextRenderer.DrawText(
                graphics,
                message,
                Font,
                new Point(
                    (ClientSize.Width - messageSize.Width) / 2,
                    (ClientSize.Height - messageSize.Height) / 2),
                EmptyTextColor);
        }
        else
        {
            var message = "没有匹配的资源";
            var messageSize = TextRenderer.MeasureText(graphics, message, Font);
            TextRenderer.DrawText(
                graphics,
                message,
                Font,
                new Point(
                    (ClientSize.Width - messageSize.Width) / 2,
                    (ClientSize.Height - messageSize.Height) / 2),
                EmptyTextColor);
        }
    }

    /// <summary>
    /// 将鼠标客户区坐标转换为筛选集合中的资源位置。
    /// </summary>
    /// <param name="clientLocation">鼠标客户区坐标。</param>
    /// <returns>命中的资源位置；未命中时返回负数。</returns>
    private int HitTest(Point clientLocation)
    {
        var virtualLocation = new Point(
            clientLocation.X - AutoScrollPosition.X,
            clientLocation.Y - AutoScrollPosition.Y);
        var column = virtualLocation.X / (CellWidth + CellMargin);
        var row = virtualLocation.Y / (CellHeight + CellMargin);
        if (column < 0 || column >= _columnCount || row < 0)
        {
            return -1;
        }

        var index = (row * _columnCount) + column;
        return index < _visibleEntries.Count ? index : -1;
    }

    /// <summary>
    /// 更新选中资源，并滚动到使其可见的行。
    /// </summary>
    /// <param name="visibleIndex">筛选集合中的目标资源位置。</param>
    /// <param name="raiseSelectionChanged">是否通知外层选择已经变化。</param>
    private void SetSelectedVisibleIndex(int visibleIndex, bool raiseSelectionChanged)
    {
        if (visibleIndex < 0 || visibleIndex >= _visibleEntries.Count || visibleIndex == _selectedVisibleIndex)
        {
            return;
        }

        var previousIndex = _selectedVisibleIndex;
        _selectedVisibleIndex = visibleIndex;
        var previousScrollPosition = AutoScrollPosition;
        ScrollSelectedEntryIntoView();
        if (previousScrollPosition != AutoScrollPosition)
        {
            Invalidate(ClientRectangle);
            RequestVisibleThumbnails();
        }
        else
        {
            if (previousIndex >= 0)
            {
                InvalidateCell(previousIndex);
            }

            InvalidateCell(_selectedVisibleIndex);
        }

        if (raiseSelectionChanged)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 让键盘导航得到的目标资源自动进入当前视口。
    /// </summary>
    private void ScrollSelectedEntryIntoView()
    {
        if (_selectedVisibleIndex < 0)
        {
            return;
        }

        var cellBounds = GetCellRectangle(_selectedVisibleIndex);
        var scrollY = -AutoScrollPosition.Y;
        var viewportBottom = scrollY + ClientSize.Height;
        if (cellBounds.Top < scrollY)
        {
            AutoScrollPosition = new Point(0, cellBounds.Top);
        }
        else if (cellBounds.Bottom > viewportBottom)
        {
            AutoScrollPosition = new Point(0, Math.Max(0, cellBounds.Bottom - ClientSize.Height));
        }
    }

    /// <summary>
    /// 根据筛选集合位置返回资源格的虚拟绘制区域。
    /// </summary>
    /// <param name="visibleIndex">资源在筛选集合中的位置。</param>
    /// <returns>资源格虚拟绘制区域。</returns>
    private Rectangle GetCellRectangle(int visibleIndex)
    {
        var row = visibleIndex / _columnCount;
        var column = visibleIndex % _columnCount;
        return new Rectangle(
            column * (CellWidth + CellMargin),
            row * (CellHeight + CellMargin),
            CellWidth + CellMargin,
            CellHeight + CellMargin);
    }

    /// <summary>
    /// 将虚拟滚动画布中的资源格区域转换为当前客户区坐标。
    /// </summary>
    /// <param name="visibleIndex">资源在筛选集合中的位置。</param>
    /// <returns>资源格在当前客户区中的绘制区域。</returns>
    private Rectangle GetCellClientRectangle(int visibleIndex)
    {
        var cellBounds = GetCellRectangle(visibleIndex);
        cellBounds.Offset(AutoScrollPosition);
        return cellBounds;
    }

    /// <summary>
    /// 按客户区坐标刷新单个资源格，避免滚动后用虚拟坐标失效导致编号残留。
    /// </summary>
    /// <param name="visibleIndex">资源在筛选集合中的位置。</param>
    private void InvalidateCell(int visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= _visibleEntries.Count)
        {
            return;
        }

        var bounds = GetCellClientRectangle(visibleIndex);
        bounds.Inflate(3, 3);
        Invalidate(bounds);
    }

    /// <summary>
    /// 计算保持图片原始比例的居中缩略图绘制区域。
    /// </summary>
    /// <param name="imageSize">缩略图原始尺寸。</param>
    /// <param name="bounds">资源格中允许绘制的区域。</param>
    /// <returns>缩略图目标区域。</returns>
    private static Rectangle GetImageBounds(Size imageSize, Rectangle bounds)
    {
        var horizontalScale = bounds.Width / (float)imageSize.Width;
        var verticalScale = bounds.Height / (float)imageSize.Height;
        // 网格仅负责居中和缩小，避免将缓存中的小尺寸缩略图再次放大。
        var scale = Math.Min(1F, Math.Min(horizontalScale, verticalScale));
        var width = Math.Max(1, (int)Math.Round(imageSize.Width * scale));
        var height = Math.Max(1, (int)Math.Round(imageSize.Height * scale));
        return new Rectangle(
            bounds.Left + ((bounds.Width - width) / 2),
            bounds.Top + ((bounds.Height - height) / 2),
            width,
            height);
    }

    /// <summary>
    /// 创建圆角矩形路径。
    /// </summary>
    /// <param name="rectangle">矩形区域。</param>
    /// <param name="radius">圆角半径。</param>
    /// <returns>GraphicsPath 对象。</returns>
    private static GraphicsPath GetRoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);

        // 左上角
        path.AddArc(arc, 180, 90);
        // 右上角
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270, 90);
        // 右下角
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // 左下角
        arc.X = rectangle.X;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 释放全部缓存的缩略图，确保 GDI 图片不会跨归档滞留。
    /// </summary>
    private void DisposeThumbnailCache()
    {
        foreach (var thumbnail in _thumbnailCache.Values)
        {
            thumbnail.Dispose();
        }

        _thumbnailCache.Clear();
    }
}
