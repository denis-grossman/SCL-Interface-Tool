using SCL_Interface_Tool.Interfaces;
using SCL_Interface_Tool.Models;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace SCL_Interface_Tool.ImageGeneration
{
    public class GdiFbdImageGenerator : IImageGenerator
    {
        public Bitmap GenerateImage(SclBlock block)
        {
            // Filter elements
            var inputs = block.Elements.Where(e => e.Direction == ElementDirection.Input).ToList();
            var outputs = block.Elements.Where(e => e.Direction == ElementDirection.Output).ToList();

            // Constants for layout
            int pinSpacing = 25;
            int headerHeight = 40;
            int blockWidth = 200;
            int pinRadius = 4;

            // Calculate canvas size
            int maxPins = Math.Max(inputs.Count, outputs.Count);
            int blockHeight = headerHeight + (maxPins * pinSpacing) + 20;
            int canvasWidth = blockWidth + 100; // Extra space for outer background
            int canvasHeight = blockHeight + 40;

            Bitmap bmp = new Bitmap(canvasWidth, canvasHeight);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);

                int startX = 50; // Center the block
                int startY = 20;

                // Draw Block Rectangle
                Rectangle rect = new Rectangle(startX, startY, blockWidth, blockHeight);
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(240, 240, 240))) // Light gray
                using (Pen borderPen = new Pen(Color.Black, 1.5f))
                {
                    g.FillRectangle(bgBrush, rect);
                    g.DrawRectangle(borderPen, rect);
                }

                // Draw Header Text
                Font boldFont = new Font("Arial", 10, FontStyle.Bold);
                Font regularFont = new Font("Arial", 9, FontStyle.Regular);
                StringFormat centerFormat = new StringFormat { Alignment = StringAlignment.Center };

                g.DrawString($"%FB???", boldFont, Brushes.Black, startX + (blockWidth / 2), startY + 5, centerFormat);
                g.DrawString($"\"{block.Name}\"", regularFont, Brushes.Black, startX + (blockWidth / 2), startY + 20, centerFormat);

                // Draw Inputs
                for (int i = 0; i < inputs.Count; i++)
                {
                    int yPos = startY + headerHeight + (i * pinSpacing);

                    // Blue Pin Circle
                    g.FillEllipse(Brushes.DodgerBlue, startX - pinRadius, yPos - pinRadius, pinRadius * 2, pinRadius * 2);
                    g.DrawEllipse(Pens.Black, startX - pinRadius, yPos - pinRadius, pinRadius * 2, pinRadius * 2);

                    // Pin Label (Left aligned)
                    g.DrawString(inputs[i].Name, regularFont, Brushes.Black, startX + 10, yPos - 7);
                }

                // Draw Outputs
                for (int i = 0; i < outputs.Count; i++)
                {
                    int yPos = startY + headerHeight + (i * pinSpacing);

                    // Green Pin Circle
                    g.FillEllipse(Brushes.LimeGreen, startX + blockWidth - pinRadius, yPos - pinRadius, pinRadius * 2, pinRadius * 2);
                    g.DrawEllipse(Pens.Black, startX + blockWidth - pinRadius, yPos - pinRadius, pinRadius * 2, pinRadius * 2);

                    // Pin Label (Right aligned)
                    StringFormat rightFormat = new StringFormat { Alignment = StringAlignment.Far };
                    g.DrawString(outputs[i].Name, regularFont, Brushes.Black, startX + blockWidth - 10, yPos - 7, rightFormat);
                }
            }
            return bmp;
        }
    }
}
