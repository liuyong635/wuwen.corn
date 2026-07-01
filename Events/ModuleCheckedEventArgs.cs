using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SeedCut
{
    public class ModuleCheckedEventArgs : EventArgs
    {
        public string ModuleName { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public bool IsCritical { get; set; }
    }
}
