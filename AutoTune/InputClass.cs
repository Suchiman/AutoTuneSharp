using System.Collections.Generic;

namespace AutoTune
{
    class InputClass
    {
        public Categorized preppedGlucose { get; set; }
        public Profile previousAutotune { get; set; }
        public Profile pumpProfile { get; set; }
    }

    public class Profile
    {
        public double min_5m_carbimpact { get; set; }
        public double dia { get; set; }
        public List<Basalprofile> basalprofile { get; set; }
        public Isfprofile isfProfile { get; set; }
        public double carb_ratio { get; set; }
        public double autosens_max { get; set; }
        public double autosens_min { get; set; }
        public string curve { get; set; }

        // unobserved
        public bool suspend_zeros_iob { get; set; }
        public double current_basal { get; set; }
        public int? min_bg { get; set; }
        public int? max_bg { get; set; }
        public bool exercise_mode { get; set; }
        public bool temptargetSet { get; set; }
        public int half_basal_exercise_target { get; set; }
        public bool useCustomPeakTime { get; set; }
        public int? insulinPeakTime { get; set; }
        public double currentBasal { get; set; }

        // unobserved - autotune core
        public int autotune_isf_adjustmentFraction { get; set; }
        public double sens { get; set; }
        public double csf { get; set; }
    }

    public class Isfprofile
    {
        public Sensitivity[] sensitivities { get; set; }
    }

    public class Sensitivity
    {
        public int i { get; set; }
        public string start { get; set; }
        public double sensitivity { get; set; }
        public int offset { get; set; }
        public int x { get; set; }
        public int endoffset { get; set; }
    }

    public class Basalprofile
    {
        public string start { get; set; }
        public int minutes { get; set; }
        public double rate { get; set; }

        // autotune core computed 0-23
        public int i { get; set; }
        public int untuned { get; set; }
    }
}