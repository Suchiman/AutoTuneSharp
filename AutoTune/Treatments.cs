using System;

namespace AutoTune
{
    public class Treatment
    {
        public string _id { get; set; }
        public string eventType { get; set; }
        public int duration { get; set; }
        public int? percent { get; set; }
        public double? rate { get; set; }
        public long pumpId { get; set; }
        public DateTimeOffset created_at { get; set; }
        public string enteredBy { get; set; }
        public long NSCLIENT_ID { get; set; }
        public int? carbs { get; set; }
        public double? insulin { get; set; }
        public long date { get; set; }
        public bool isSMB { get; set; }
        public double? absolute { get; set; } // WTF: nullable to emulate undefined
        public double originalExtendedAmount { get; set; }
        public int glucose { get; set; }
        public string glucoseType { get; set; }
        public Boluscalc boluscalc { get; set; }
        public string reason { get; set; }
        public int targetBottom { get; set; }
        public int targetTop { get; set; }
        public string units { get; set; }
        public string profile { get; set; }
        public string profileJson { get; set; }
        public string profilePlugin { get; set; }
        public string notes { get; set; }
        public bool isAnnouncement { get; set; }
        public int preBolus { get; set; }

        // unobserved fields
        public long? timestamp { get; set; }
        public string _type { get; set; }
        public double? amount { get; set; }
    }

    public class Boluscalc
    {
        public string profile { get; set; }
        public string notes { get; set; }
        public DateTimeOffset eventTime { get; set; }
        public int targetBGLow { get; set; }
        public int targetBGHigh { get; set; }
        public double isf { get; set; }
        public double ic { get; set; }
        public double iob { get; set; }
        public double bolusiob { get; set; }
        public double basaliob { get; set; }
        public bool bolusiobused { get; set; }
        public bool basaliobused { get; set; }
        public int bg { get; set; }
        public double insulinbg { get; set; }
        public bool insulinbgused { get; set; }
        public int bgdiff { get; set; }
        public double insulincarbs { get; set; }
        public int carbs { get; set; }
        public double cob { get; set; }
        public bool cobused { get; set; }
        public double insulincob { get; set; }
        public double othercorrection { get; set; }
        public double insulinsuperbolus { get; set; }
        public double insulintrend { get; set; }
        public double insulin { get; set; }
        public bool superbolusused { get; set; }
        public bool trendused { get; set; }
        public double trend { get; set; }
        public bool ttused { get; set; }
        public int percentageCorrection { get; set; }
    }
}
