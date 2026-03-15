using System.Drawing;

namespace InventorAutosave.UI;

internal static class CommandIconFactory
{
    public static object CreateSmallIcon(string label, Color background)
    {
        return PictureConverter.ToPictureDisp(CreateBitmap(label, background, 16, 7f));
    }

    public static object CreateLargeIcon(string label, Color background)
    {
        return PictureConverter.ToPictureDisp(CreateBitmap(label, background, 32, 12f));
    }

    private static Bitmap CreateBitmap(string label, Color background, int size, float fontSize)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(background);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        graphics.DrawString(label, font, brush, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }
}
