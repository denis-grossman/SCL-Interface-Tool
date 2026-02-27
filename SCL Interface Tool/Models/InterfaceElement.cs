using System;
using System.Collections.Generic;
using System.Text;

namespace SCL_Interface_Tool.Models
{
    public class InterfaceElement
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public ElementDirection Direction { get; set; }
        public string InitialValue { get; set; }
        public string Comment { get; set; }
        public string Attributes { get; set; }
    }
}
