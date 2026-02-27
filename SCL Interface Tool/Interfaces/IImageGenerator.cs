using SCL_Interface_Tool.Models;
using System.Drawing;

namespace SCL_Interface_Tool.Interfaces
{
    public interface IImageGenerator
    {
        // Added the showComments boolean toggle
        Bitmap GenerateImage(SclBlock block, bool showComments);
    }
}
