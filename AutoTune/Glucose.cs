using System;

namespace AutoTune
{
    public class Glucose
    {
        public string _id { get; set; }
        public string device { get; set; }
        public long date { get; set; }
        public DateTimeOffset dateString { get; set; }
        public int sgv { get; set; }
        public float delta { get; set; }
        public string direction { get; set; }
        public string type { get; set; }
        public int filtered { get; set; }
        public int unfiltered { get; set; }
        public int rssi { get; set; }
        public int noise { get; set; }
        public DateTimeOffset sysTime { get; set; }
        public int utcOffset { get; set; }

        // unobserved
        public int? glucose { get; set; }
        public string displayTime { get; set; }
        public double avgDelta { get; set; }
        public double BGI { get; set; }
        public double deviation { get; set; }
        public string mealAbsorption { get; set; }
        public int mealCarbs { get; set; }
        public string uamAbsorption { get; set; }
    }
}
