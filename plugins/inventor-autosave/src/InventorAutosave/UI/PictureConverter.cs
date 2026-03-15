using System.Drawing;
using System.Windows.Forms;

namespace InventorAutosave.UI;

internal sealed class PictureConverter : AxHost
{
    private PictureConverter()
        : base(string.Empty)
    {
    }

    public static object ToPictureDisp(Image image)
    {
        return GetIPictureDispFromPicture(image)!;
    }
}
