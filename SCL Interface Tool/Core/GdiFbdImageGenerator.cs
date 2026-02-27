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
        public Bitmap GenerateImage(SclBlock block, bool showComments)
        {
            var inputs = block.Elements.Where(e => e.Direction == ElementDirection.Input).ToList();
            var outputs = block.Elements.Where(e => e.Direction == ElementDirection.Output).ToList();

            // Layout Constants
            int headerHeight = 45;
            int blockWidth = 140;
            int lineLength = 25;
            int minRowHeight = 24;

            // Comment constraints
            int maxCommentWidth = 250;
            Font commentFont = new Font("Segoe UI", 8.5f, FontStyle.Italic);
            int maxCommentHeight = commentFont.Height * 3 + 2; // Exact height to fit max 3 lines

            int maxRows = Math.Max(inputs.Count + 1, outputs.Count + 1);
            int[] rowHeights = new int[maxRows];

            // -------------------------------------------------------------
            // PASS 1: Calculate Dynamic Row Heights based on Comment Length
            // -------------------------------------------------------------
            using (Graphics gMeasure = Graphics.FromImage(new Bitmap(1, 1)))
            {
                for (int r = 0; r < maxRows; r++)
                {
                    int requiredHeight = minRowHeight;
                    if (showComments)
                    {
                        string leftCmt = (r > 0 && r - 1 < inputs.Count && !string.IsNullOrWhiteSpace(inputs[r - 1].Comment)) ? "// " + inputs[r - 1].Comment : "";
                        string rightCmt = (r < outputs.Count && !string.IsNullOrWhiteSpace(outputs[r].Comment)) ? "// " + outputs[r].Comment : "";

                        if (!string.IsNullOrEmpty(leftCmt))
                        {
                            SizeF s = gMeasure.MeasureString(leftCmt, commentFont, maxCommentWidth);
                            requiredHeight = Math.Max(requiredHeight, (int)Math.Min(s.Height + 8, maxCommentHeight + 8));
                        }
                        if (!string.IsNullOrEmpty(rightCmt))
                        {
                            SizeF s = gMeasure.MeasureString(rightCmt, commentFont, maxCommentWidth);
                            requiredHeight = Math.Max(requiredHeight, (int)Math.Min(s.Height + 8, maxCommentHeight + 8));
                        }
                    }
                    rowHeights[r] = requiredHeight;
                }
            }

            int blockHeight = headerHeight + rowHeights.Sum() + 10;

            // Dynamically scale canvas width
            int leftArea = showComments ? maxCommentWidth + 60 : 100;
            int rightArea = showComments ? maxCommentWidth + 60 : 100;
            int canvasWidth = leftArea + blockWidth + rightArea;
            int canvasHeight = blockHeight + 70;

            Bitmap bmp = new Bitmap(canvasWidth, canvasHeight);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.White);

                int startX = leftArea;
                int startY = 40;

                // 1. Draw Block Body Background
                Rectangle bodyRect = new Rectangle(startX, startY + headerHeight, blockWidth, blockHeight - headerHeight);
                using (Brush bodyBrush = new SolidBrush(Color.FromArgb(230, 233, 238)))
                {
                    g.FillRectangle(bodyBrush, bodyRect);
                }

                // 2. Draw Header Background
                Rectangle headerRect = new Rectangle(startX, startY, blockWidth, headerHeight);
                using (Brush headerBrush = new SolidBrush(Color.FromArgb(204, 213, 223)))
                {
                    g.FillRectangle(headerBrush, headerRect);
                }

                // 3. Draw Block Outline
                g.DrawRectangle(Pens.DarkGray, new Rectangle(startX, startY, blockWidth, blockHeight));

                Font fbNumFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                Font fbNameFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                Font pinFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                Font valFont = new Font("Segoe UI", 8f, FontStyle.Regular);
                Brush commentBrush = new SolidBrush(Color.FromArgb(34, 139, 34)); // TIA Green

                StringFormat centerFormat = new StringFormat { Alignment = StringAlignment.Center };
                StringFormat rightFormat = new StringFormat { Alignment = StringAlignment.Far };

                // Formats for truncating multi-line comments with "..."
                StringFormat leftCommentFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                StringFormat rightCommentFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

                // 4. Draw <iDB_Name> Instance DB box for FBs
                if (block.BlockType == "FUNCTION_BLOCK")
                {
                    int instBoxHeight = 18;
                    int instBoxY = startY - instBoxHeight - 4;
                    using (Pen dashPen = new Pen(Color.Gray, 1f) { DashStyle = DashStyle.Dash })
                    {
                        g.DrawRectangle(dashPen, startX, instBoxY, blockWidth, instBoxHeight);
                    }
                    g.DrawString($"\"iDB_{block.Name}\"", fbNameFont, Brushes.Black, startX + (blockWidth / 2), instBoxY + 1, centerFormat);
                }

                // 5. Draw Header Text
                string prefix = block.BlockType == "FUNCTION_BLOCK" ? "%FB" : (block.BlockType == "FUNCTION" ? "%FC" : $"%{block.BlockType}");
                using (Brush tealBrush = new SolidBrush(Color.FromArgb(0, 139, 139)))
                {
                    g.DrawString($"{prefix}???", fbNumFont, tealBrush, startX + (blockWidth / 2), startY + 5, centerFormat);
                }
                g.DrawString($"\"{block.Name}\"", fbNameFont, Brushes.Black, startX + (blockWidth / 2), startY + 22, centerFormat);

                // Calculate Y Centers for each row
                int[] rowYCenters = new int[maxRows];
                int currentY = startY + headerHeight + 5; // Top padding inside block
                for (int r = 0; r < maxRows; r++)
                {
                    rowYCenters[r] = currentY + (rowHeights[r] / 2);
                    currentY += rowHeights[r];
                }

                // 6. Draw Pins
                using (Pen linePen = new Pen(Color.Black, 1.5f))
                {
                    // EN (Enable)
                    int enYPos = rowYCenters[0];
                    g.DrawLine(linePen, startX - lineLength, enYPos, startX, enYPos);
                    g.DrawString("...", pinFont, Brushes.Black, startX - lineLength - 16, enYPos - 12);
                    g.DrawString("EN", pinFont, Brushes.Black, startX + 5, enYPos - 8);

                    // Inputs
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        var el = inputs[i];
                        int yPos = rowYCenters[i + 1];

                        g.DrawLine(linePen, startX - lineLength, yPos, startX, yPos);
                        g.DrawString("...", pinFont, Brushes.Black, startX - lineLength - 16, yPos - 12);
                        g.FillEllipse(Brushes.DodgerBlue, startX - lineLength - 3, yPos - 3, 6, 6);
                        g.DrawString(el.Name, pinFont, Brushes.Black, startX + 5, yPos - 8);

                        if (!string.IsNullOrEmpty(el.InitialValue))
                        {
                            g.DrawString(el.InitialValue, valFont, Brushes.DimGray, startX - lineLength, yPos - 18);
                        }

                        if (showComments && !string.IsNullOrWhiteSpace(el.Comment))
                        {
                            RectangleF cmtRect = new RectangleF(startX - lineLength - 20 - maxCommentWidth, yPos - (maxCommentHeight / 2), maxCommentWidth, maxCommentHeight);
                            g.DrawString($"// {el.Comment}", commentFont, commentBrush, cmtRect, leftCommentFormat);

                            // Widen bounding box so hovering over the comment also shows the yellow hint
                            el.DisplayBounds = new Rectangle((int)cmtRect.X, yPos - (rowHeights[i + 1] / 2), maxCommentWidth + lineLength + 60, rowHeights[i + 1]);
                        }
                        else
                        {
                            el.DisplayBounds = new Rectangle(startX - lineLength - 20, yPos - (rowHeights[i + 1] / 2), lineLength + 60, rowHeights[i + 1]);
                        }
                    }

                    // Outputs
                    for (int i = 0; i < outputs.Count; i++)
                    {
                        var el = outputs[i];
                        int yPos = rowYCenters[i];

                        g.DrawLine(linePen, startX + blockWidth, yPos, startX + blockWidth + lineLength, yPos);
                        g.DrawString("...", pinFont, Brushes.Black, startX + blockWidth + lineLength + 2, yPos - 12);
                        g.FillEllipse(Brushes.LimeGreen, startX + blockWidth + lineLength - 3, yPos - 3, 6, 6);
                        g.DrawString(el.Name, pinFont, Brushes.Black, startX + blockWidth - 5, yPos - 8, rightFormat);

                        if (!string.IsNullOrEmpty(el.InitialValue))
                        {
                            g.DrawString(el.InitialValue, valFont, Brushes.DimGray, startX + blockWidth + 5, yPos - 18);
                        }

                        if (showComments && !string.IsNullOrWhiteSpace(el.Comment))
                        {
                            RectangleF cmtRect = new RectangleF(startX + blockWidth + lineLength + 20, yPos - (maxCommentHeight / 2), maxCommentWidth, maxCommentHeight);
                            g.DrawString($"// {el.Comment}", commentFont, commentBrush, cmtRect, rightCommentFormat);

                            el.DisplayBounds = new Rectangle(startX + blockWidth - 40, yPos - (rowHeights[i] / 2), maxCommentWidth + lineLength + 60, rowHeights[i]);
                        }
                        else
                        {
                            el.DisplayBounds = new Rectangle(startX + blockWidth - 40, yPos - (rowHeights[i] / 2), lineLength + 60, rowHeights[i]);
                        }
                    }

                    // ENO (Enable Out)
                    int enoYPos = rowYCenters[outputs.Count];
                    g.DrawLine(linePen, startX + blockWidth, enoYPos, startX + blockWidth + lineLength, enoYPos);
                    g.DrawString("...", pinFont, Brushes.Black, startX + blockWidth + lineLength + 2, enoYPos - 12);
                    g.DrawString("ENO", pinFont, Brushes.Black, startX + blockWidth - 5, enoYPos - 8, rightFormat);
                }

                commentBrush.Dispose();
                commentFont.Dispose();
            }
            return bmp;
        }
    }
}
