using System.Collections.Generic;

namespace AutoTune
{
    class Prepare
    {
        public List<Treatment> history { get; set; }
        public Profile profile { get; set; }
        public Profile pumpprofile { get; set; }
        public List<object> carbs { get; set; }
        public List<Glucose> glucose { get; set; }
        public bool categorize_uam_as_basal { get; set; }
        public bool tune_insulin_curve { get; set; }
    }
}