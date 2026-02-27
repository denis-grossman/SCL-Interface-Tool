using SCL_Interface_Tool.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SCL_Interface_Tool.Interfaces
{
    public interface ISclParser
    {
        // Returns a list of parsed blocks. Warnings/Errors are written to an out parameter or event.
        List<SclBlock> Parse(string sclText, out List<string> errors);
    }
}
