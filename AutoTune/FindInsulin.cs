using System;
using System.Collections.Generic;

namespace AutoTune
{
    class FindInsulin
    {
        public Profile profile { get; set; }
        public List<Treatment> history { get; set; }
        public List<Treatment> history24 { get; set; }
        public DateTimeOffset clock { get; set; }
        public AutoSense autosens { get; set; }
    }

    public class AutoSense
    {
        public float ratio { get; set; }
    }
}