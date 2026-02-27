using System;
using System.Collections.Generic;
using System.Text;

namespace SCL_Interface_Tool.Models
{
    public class SclBlock
    {
        public string BlockType { get; set; } // FUNCTION_BLOCK, FUNCTION, DATA_BLOCK, TYPE
        public string Name { get; set; }
        public string Title { get; set; }
        public System.Collections.Generic.List<InterfaceElement> Elements { get; set; } = new();
    }
}
