using System;
using System.Collections.Generic;
using System.Text;

namespace SCL_Interface_Tool.Models
{
    public class SclBlock
    {
        public string BlockType { get; set; } // "FUNCTION_BLOCK" or "FUNCTION"
        public string Name { get; set; }
        public string Title { get; set; }
        public List<InterfaceElement> Elements { get; set; } = new();
    }
}
