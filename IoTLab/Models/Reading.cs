using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IoTLab.Models
{
    public class Reading
    {
        public DateTime ReadingDateTime { get; set; }

        public double Temperature { get; set; }

        public double Light { get; set; }

        public bool Control { get; set; }
    }
}
