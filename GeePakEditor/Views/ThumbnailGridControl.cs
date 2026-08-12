using System.Drawing.Drawing2D;
using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// 使用固定单元格虚拟化显示 PAK 资源缩略图的滚动网格。
/// </summary>
internal sealed class ThumbnailGridControl : ScrollableControl
{
    /// <summary>
    /// 每个资源格的固定宽度，与原编辑器的缩略图布局保持接近。
    /// </summary>
    private const int CellWidth = 101;

    /// <summary>
    /// 每个资源格的固定高度，底部预留索引文本空间。
    /// </summary>
    private const int CellHeight = 101;

    /// <summary>
    /// 资源格内缩略图的边距。
    /// </summary>
    private const int ThumbnailPadding = 8;

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
    /// 当前按列排列的资源格数量。
    /// </summary>
    private int _columnCount = 1;

    /// <summary>
    /// 当前资源筛选文本，用于列表刷新后保持用户正在使用的筛选条件。
    /// </summary>
    private string _filterText = string.Empty;

    /// <summary>
    /// 初始化支持键盘导航、滚动和无闪烁绘制的资源网格。
    /// </summary>
    public ThumbnailGridControl()
    {
        AutoScroll = true;
        BackColor = Color.White;
        DoubleBuffered = true;
        TabStop = true;
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
        Invalidate();
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
    /// 绘制固定尺寸的资源单元格、缩略图、选中状态和五位索引。
    /// </summary>
    /// <param name="e">控件绘制上下文。</param>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_visibleEntries.Count == 0)
        {
            return;
        }

        var scrollOffset = AutoScrollPosition;
        e.Graphics.TranslateTransform(scrollOffset.X, scrollOffset.Y);
        var (firstIndex, lastIndex) = GetVisibleRange();
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            DrawCell(e.Graphics, index, GetCellRectangle(index));
        }

        e.Graphics.ResetTransform();
    }

    /// <summary>
    /// 根据鼠标位置更新当前选择。
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
        _columnCount = Math.Max(1, ClientSize.Width / CellWidth);
        var rowCount = (int)Math.Ceiling(_visibleEntries.Count / (double)_columnCount);
        AutoScrollMinSize = new Size(
            Math.Max(ClientSize.Width, _columnCount * CellWidth),
            Math.Max(ClientSize.Height, rowCount * CellHeight));
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
        var firstRow = Math.Max(0, (scrollY / CellHeight) - 1);
        var lastRow = Math.Min(
            (int)Math.Ceiling(_visibleEntries.Count / (double)_columnCount) - 1,
            ((scrollY + ClientSize.Height) / CellHeight) + 1);
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
    /// 绘制单个资源格及其缩略图和索引标签。
    /// </summary>
    /// <param name="graphics">控件绘制上下文。</param>
    /// <param name="visibleIndex">资源在筛选集合中的位置。</param>
    /// <param name="cellBounds">资源格的虚拟绘制区域。</param>
    private void DrawCell(Graphics graphics, int visibleIndex, Rectangle cellBounds)
    {
        var entry = _visibleEntries[visibleIndex];
        var selected = visibleIndex == _selectedVisibleIndex;
        using var backgroundBrush = new SolidBrush(selected ? Color.FromArgb(0, 120, 215) : Color.White);
        using var borderPen = new Pen(Color.FromArgb(205, 205, 205));
        graphics.FillRectangle(backgroundBrush, cellBounds);
        graphics.DrawRectangle(borderPen, cellBounds.Left, cellBounds.Top, cellBounds.Width - 1, cellBounds.Height - 1);

        var thumbnailBounds = new Rectangle(
            cellBounds.Left + ThumbnailPadding,
            cellBounds.Top + ThumbnailPadding,
            cellBounds.Width - (ThumbnailPadding * 2),
            cellBounds.Height - 30);
        CheckerboardPreviewControl.DrawCheckerboard(graphics, thumbnailBounds);
        if (_thumbnailCache.TryGetValue(entry.Index, out var thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(thumbnail, GetImageBounds(thumbnail.Size, thumbnailBounds));
        }

        var textBounds = new Rectangle(cellBounds.Left + 2, cellBounds.Bottom - 20, cellBounds.Width - 4, 18);
        TextRenderer.DrawText(
            graphics,
            entry.Index.ToString("D5"),
            Font,
            textBounds,
            selected ? Color.White : Color.FromArgb(45, 45, 45),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
        var column = virtualLocation.X / CellWidth;
        var row = virtualLocation.Y / CellHeight;
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
        ScrollSelectedEntryIntoView();
        if (previousIndex >= 0)
        {
            Invalidate(GetCellRectangle(previousIndex));
        }

        Invalidate(GetCellRectangle(_selectedVisibleIndex));
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
        return new Rectangle(column * CellWidth, row * CellHeight, CellWidth, CellHeight);
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
