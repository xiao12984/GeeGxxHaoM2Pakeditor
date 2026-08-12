namespace GeePakEditor.Views;

/// <summary>
/// 在透明棋盘格背景上按原始像素尺寸显示当前选中图片的预览控件。
/// </summary>
internal sealed class CheckerboardPreviewControl : Control
{
    /// <summary>
    /// 透明背景中浅色格的颜色。
    /// </summary>
    private static readonly Color LightSquareColor = Color.FromArgb(245, 245, 245);

    /// <summary>
    /// 透明背景中深色格的颜色。
    /// </summary>
    private static readonly Color DarkSquareColor = Color.FromArgb(190, 190, 190);

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
        // 尺寸变化会改变图片居中坐标，必须使整个区域失效而非只刷新局部。
        Invalidate(ClientRectangle);
    }

    /// <summary>
    /// 先绘制棋盘格，再以 1:1 原始像素尺寸居中显示资源。
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

        var location = new Point(
            (ClientSize.Width - _image.Width) / 2,
            (ClientSize.Height - _image.Height) / 2);
        // DrawImageUnscaled 保持资源的实际像素尺寸；超出预览区域时由控件裁切。
        e.Graphics.DrawImageUnscaled(_image, location);
    }

    /// <summary>
    /// 绘制与原资源编辑器一致的灰白透明棋盘格。
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
