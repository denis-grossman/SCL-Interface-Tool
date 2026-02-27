using System;
using System.Collections.Generic;
using System.Text;

namespace SCL_Interface_Tool.Models
{
    public class InterfaceElement
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string DataType { get; set; }
        public ElementDirection Direction { get; set; }
        public string InitialValue { get; set; }
        public string Comment { get; set; }
        public string Attributes { get; set; }

        // Stores the physical area on the generated image for ToolTips
        public Rectangle DisplayBounds { get; set; }
    }
}
