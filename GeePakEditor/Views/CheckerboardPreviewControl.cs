namespace GeePakEditor.Views;

/// <summary>
/// 在透明棋盘格背景上按原始像素尺寸显示当前选中图片的现代化预览控件。
/// </summary>
internal sealed class CheckerboardPreviewControl : ScrollableControl
{
    /// <summary>
    /// 透明背景中浅色格的颜色（柔化版）。
    /// </summary>
    private static readonly Color LightSquareColor = Color.FromArgb(248, 248, 248);

    /// <summary>
    /// 透明背景中深色格的颜色（柔化版）。
    /// </summary>
    private static readonly Color DarkSquareColor = Color.FromArgb(225, 225, 225);

    /// <summary>
    /// 当前由主窗口托管和释放的预览图片。
    /// </summary>
    private Image? _image;

    /// <summary>
    /// 初始化具有双缓冲能力的透明预览区域。
    /// </summary>
    public CheckerboardPreviewControl()
    {
        // 将背景、棋盘格和图片合并到同一双缓冲绘制帧，避免缩放布局后残留旧图像。
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.Opaque,
            true);
        // 图片超过可视范围时由 WinForms 自动显示滚动条，保持预览资源的原始像素尺寸。
        AutoScroll = true;
        AutoScrollMargin = Size.Empty;
        DoubleBuffered = true;
        BackColor = Color.White;
        TabStop = false;
    }

    /// <summary>
    /// 获取或设置当前预览图片；图片的释放责任由调用方承担。
    /// </summary>
    public Image? Image
    {
        get => _image;
        set
        {
            _image = value;
            UpdateScrollMetrics(resetScrollPosition: true);
            // 图片切换时请求整个预览区域重绘，确保旧图片占用的区域被棋盘格覆盖。
            Invalidate(ClientRectangle);
        }
    }

    /// <summary>
    /// 禁用默认背景分帧绘制，由 OnPaint 在同一缓冲帧中完整绘制棋盘格和图片。
    /// </summary>
    /// <param name="e">背景绘制上下文。</param>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 控件区域会由 OnPaint 的棋盘格完整覆盖，无需执行默认背景绘制。
    }

    /// <summary>
    /// 预览区域尺寸变化后请求完整重绘，避免拆分条或 DPI 缩放留下旧图片像素。
    /// </summary>
    /// <param name="e">控件尺寸变化参数。</param>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollMetrics(resetScrollPosition: false);
        // 尺寸变化会改变图片居中坐标，必须使整个区域失效而非只刷新局部。
        Invalidate(ClientRectangle);
    }

    /// <summary>
    /// 滚动条位置变化后重绘预览，确保图片按新的视口偏移显示。
    /// </summary>
    /// <param name="se">滚动参数。</param>
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // 预览采用自定义绘制，需要主动刷新整个客户区以应用最新滚动偏移。
        Invalidate(ClientRectangle);
    }

    /// <summary>
    /// 先绘制柔化棋盘格，再以 1:1 原始像素尺寸居中显示资源。
    /// </summary>
    /// <param name="e">控件绘制上下文。</param>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawCheckerboard(e.Graphics, ClientRectangle);
        if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var graphicsState = e.Graphics.Save();
        try
        {
            // AutoScrollPosition 为负值，将虚拟图片画布平移到当前滚动视口。
            e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            // DrawImageUnscaled 保持资源的实际像素尺寸；超出预览区域时可通过滚动条查看。
            e.Graphics.DrawImageUnscaled(_image, GetImageLocation());
        }
        finally
        {
            e.Graphics.Restore(graphicsState);
        }
    }

    /// <summary>
    /// 根据当前图片尺寸更新预览画布范围，并在切换图片时回到左上角。
    /// </summary>
    /// <param name="resetScrollPosition">是否将滚动位置重置为左上角。</param>
    private void UpdateScrollMetrics(bool resetScrollPosition)
    {
        var minimumSize = _image is null ? Size.Empty : _image.Size;
        if (AutoScrollMinSize != minimumSize)
        {
            // 虚拟画布与原图同尺寸，只有图片超出客户区时才会显示对应方向的滚动条。
            AutoScrollMinSize = minimumSize;
        }

        if (resetScrollPosition)
        {
            // 切换资源后默认从左上角查看，避免沿用上一张大图的滚动位置。
            AutoScrollPosition = Point.Empty;
        }
    }

    /// <summary>
    /// 获取图片在虚拟画布中的原始像素绘制位置。
    /// </summary>
    /// <returns>原图的绘制起点。</returns>
    private Point GetImageLocation()
    {
        if (_image is null)
        {
            return Point.Empty;
        }

        // 仅在该方向无需滚动时居中，超出视口的方向从画布原点开始绘制。
        var horizontalPosition = _image.Width <= ClientSize.Width
            ? (ClientSize.Width - _image.Width) / 2
            : 0;
        var verticalPosition = _image.Height <= ClientSize.Height
            ? (ClientSize.Height - _image.Height) / 2
            : 0;
        return new Point(horizontalPosition, verticalPosition);
    }

    /// <summary>
    /// 绘制柔化的灰白透明棋盘格，与原资源编辑器视觉风格一致但更柔和。
    /// </summary>
    /// <param name="graphics">控件绘制上下文。</param>
    /// <param name="bounds">需要填充的区域。</param>
    internal static void DrawCheckerboard(Graphics graphics, Rectangle bounds)
    {
        const int squareSize = 16;
        for (var row = bounds.Top; row < bounds.Bottom; row += squareSize)
        {
            for (var column = bounds.Left; column < bounds.Right; column += squareSize)
            {
                var columnIndex = (column - bounds.Left) / squareSize;
                var rowIndex = (row - bounds.Top) / squareSize;
                var color = (columnIndex + rowIndex) % 2 == 0 ? LightSquareColor : DarkSquareColor;
                using var brush = new SolidBrush(color);
                graphics.FillRectangle(
                    brush,
                    column,
                    row,
                    Math.Min(squareSize, bounds.Right - column),
                    Math.Min(squareSize, bounds.Bottom - row));
            }
        }
    }

}