using SCL_Interface_Tool.Models;
using System.Drawing;

namespace SCL_Interface_Tool.Interfaces
{
    public interface IImageGenerator
    {
        Bitmap GenerateImage(SclBlock block);
    }
}
