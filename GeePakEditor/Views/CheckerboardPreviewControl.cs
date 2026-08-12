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
            Invalidate();
        }
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
