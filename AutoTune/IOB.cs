using System;
using System.Collections.Generic;

namespace AutoTune
{
    static class IOB
    {
        public static List<TotalIOB> getIOB(FindInsulin inputs, bool currentIOBOnly = false, List<TempData> treatments = null)
        {
            List<TempData> treatmentsWithZeroTemp;
            if (treatments == null)
            {
                treatments = History.find_insulin(inputs);
                // calculate IOB based on continuous future zero temping as well
                treatmentsWithZeroTemp = History.find_insulin(inputs, 240);
            }
            else
            {
                treatmentsWithZeroTemp = new List<TempData>();
            }
            //Console.WriteLine(treatments.length, treatmentsWithZeroTemp.length);
            //Console.WriteLine(treatments[treatments.length-1], treatmentsWithZeroTemp[treatmentsWithZeroTemp.length-1])

            var iobArray = new List<TotalIOB>();
            //Console.WriteLine(inputs.clock);
            //if (!Regex.IsMatch(inputs.clock, @"(Z|[+-][0-2][0-9]:?[034][05])+"))
            //{
            //    Console.WriteLine("Warning: clock input " + inputs.clock + " is unzoned; please pass clock-zoned.json instead");
            //}
            var clock = inputs.clock;

            long lastBolusTime = 0; // WTF: new Date(0).getTime(); //clock.getTime());
            var lastTemp = new TempHistory { date = 0 }; // WTF: new Date(0).getTime(); //clock.getTime());
                                                         //Console.WriteLine(treatments[treatments.length-1]);
            foreach (var treatment in treatments)
            {
                if (treatment is TempBolus { insulin: { } } tb) // WTF: insulin can be still null here -> (treatment.insulin && treatment.started_at)
                {
                    lastBolusTime = Math.Max(lastBolusTime, tb.started_at.ToUnixTimeMilliseconds());
                    //Console.WriteLine(treatment.insulin,treatment.started_at,lastBolusTime);
                }
                else if (treatment is TempHistory { rate: { } } th && th.duration > 0) // WTF: nullability (treatment.rate is "number" && treatment.duration)
                {
                    if (th.date > lastTemp.date)
                    {
                        lastTemp = th;
                        lastTemp.duration = JSMath.Round(lastTemp.duration, 2); // WTF?
                    }

                    //Console.WriteLine(treatment.rate, treatment.duration, treatment.started_at,lastTemp.started_at)
                }
                //Console.WriteLine(treatment.rate, treatment.duration, treatment.started_at,lastTemp.started_at)
                //if (treatment.insulin && treatment.started_at) { Console.WriteLine(treatment.insulin,treatment.started_at,lastBolusTime); }
            }

            int iStop;
            if (currentIOBOnly)
            {
                // for COB calculation, we only need the zeroth element of iobArray
                iStop = 1;
            }
            else
            {
                // predict IOB out to 4h, regardless of DIA
                iStop = 4 * 60;
            }

            for (var i = 0; i < iStop; i += 5)
            {
                var t = clock.AddMinutes(i);
                //Console.WriteLine(t);
                var iob = iobTotal(treatments, inputs.profile, t);
                var iobWithZeroTemp = iobTotal(treatmentsWithZeroTemp, inputs.profile, t);
                //Console.WriteLine(opts.treatments[opts.treatments.length-1], optsWithZeroTemp.treatments[optsWithZeroTemp.treatments.length-1])
                iobArray.Add(iob);
                //Console.WriteLine(iob.iob, iobWithZeroTemp.iob);
                //Console.WriteLine(iobArray.length-1, iobArray[iobArray.length-1]);
                iobArray[iobArray.Count - 1].iobWithZeroTemp = iobWithZeroTemp;
            }
            //Console.WriteLine(lastBolusTime);
            iobArray[0].lastBolusTime = lastBolusTime;
            iobArray[0].lastTemp = lastTemp;
            return iobArray;
        }

        static TotalIOB iobTotal(List<TempData> treatments, Profile profile_data, DateTimeOffset time)
        {
            var now = time.ToUnixTimeMilliseconds();
            var dia = profile_data.dia;
            var peak = 0;
            double iob = 0;
            double basaliob = 0;
            double bolusiob = 0;
            double netbasalinsulin = 0;
            double bolusinsulin = 0;
            //var bolussnooze = 0;
            double activity = 0;
            if (treatments == null) return null;
            //if (typeof time == 'undefined') {
            //var time = new Date();
            //}

            // force minimum DIA of 3h
            if (dia < 3)
            {
                //Console.WriteLine("Warning; adjusting DIA from",dia,"to minimum of 3 hours");
                dia = 3;
            }

            var curveDefaults = new Dictionary<string, (bool requireLongDia, int peak, int tdMin)>
            {
                ["bilinear"] = (requireLongDia: false, peak: 75 /*not really used, but prevents having to check later*/, tdMin: 0),
                ["rapid-acting"] = (requireLongDia: true, peak: 75, tdMin: 300),
                ["ultra-rapid"] = (requireLongDia: true, peak: 55, tdMin: 300)
            };

            var curve = "bilinear";

            if (profile_data.curve != null)
            {
                curve = profile_data.curve.ToLower();
            }

            if (!curveDefaults.ContainsKey(curve))
            {
                Console.WriteLine("Unsupported curve function: \"" + curve + "\". Supported curves: \"bilinear\", \"rapid-acting\" (Novolog, Novorapid, Humalog, Apidra) and \"ultra-rapid\" (Fiasp). Defaulting to \"rapid-acting\".");
                curve = "rapid-acting";
            }

            var defaults = curveDefaults[curve];

            // Force minimum of 5 hour DIA when default requires a Long DIA.
            if (defaults.requireLongDia && dia < 5)
            {
                //Console.WriteLine('Pump DIA must be set to 5 hours or more with the new curves, please adjust your pump. Defaulting to 5 hour DIA.');
                dia = 5;
            }

            peak = defaults.peak;

            foreach (var treatment in treatments)
            {
                if (treatment.date <= now)
                {
                    var dia_ago = now - dia * 60 * 60 * 1000;
                    if (treatment.date > dia_ago && treatment is TempBolus tb && iobCalc(treatment, time, curve, dia, peak, profile_data) is { } tIOB)
                    {
                        // tIOB = total IOB
                        iob += tIOB.iobContrib;
                        activity += tIOB.activityContrib;
                        // basals look like either of these:
                        // {"insulin":-0.05,"date":1507265512363.6365,"created_at":"2017-10-06T04:51:52.363Z"}
                        // {"insulin":0.05,"date":1507266530000,"created_at":"2017-10-06T05:08:50.000Z"}
                        // boluses look like:
                        // {"timestamp":"2017-10-05T22:06:31-07:00","started_at":"2017-10-06T05:06:31.000Z","date":1507266391000,"insulin":0.5}
                        if (tb.insulin < 0.1)
                        {
                            basaliob += tIOB.iobContrib;
                            netbasalinsulin += tb.insulin.Value;
                        }
                        else
                        {
                            bolusiob += tIOB.iobContrib;
                            bolusinsulin += tb.insulin.Value;
                        }
                        //Console.WriteLine(JSON.stringify(treatment));
                    }
                } // else { Console.WriteLine("ignoring future treatment:",treatment); }
            }

            return new TotalIOB
            {
                iob = JSMath.Round(iob, 3),
                activity = JSMath.Round(activity, 4),
                basaliob = JSMath.Round(basaliob, 3),
                bolusiob = JSMath.Round(bolusiob, 3),
                netbasalinsulin = JSMath.Round(netbasalinsulin, 3),
                bolusinsulin = JSMath.Round(bolusinsulin, 3),
                time = time
            };
        }

        static IOBCalc iobCalc(TempData treatment, DateTimeOffset time, string curve, double dia, int peak, Profile profile)
        {
            // iobCalc returns two variables:
            //   activityContrib = units of treatment.insulin used in previous minute
            //   iobContrib = units of treatment.insulin still remaining at a given point in time
            // ("Contrib" is used because these are the amounts contributed from pontentially multiple treatment.insulin dosages -- totals are calculated in total.js)
            //
            // Variables can be calculated using either:
            //   A bilinear insulin action curve (which only takes duration of insulin activity (dia) as an input parameter) or
            //   An exponential insulin action curve (which takes both a dia and a peak parameter)
            // (which functional form to use is specified in the user's profile)

            if (treatment is TempBolus { insulin: { } } tb) // WTF: insulin nullability // (treatment.insulin)
            {
                // Calc minutes since bolus (minsAgo)
                //if (typeof time == 'undefined')
                //{
                //    time = new Date();
                //}
                var bolusTime = DateTimeOffset.FromUnixTimeMilliseconds(tb.date).ToLocalTime();
                var minsAgo = JSMath.Round((time - bolusTime).TotalMinutes);

                if (curve == "bilinear")
                {
                    return iobCalcBilinear(tb, minsAgo, dia);  // no user-specified peak with this model
                }
                else
                {
                    return iobCalcExponential(tb, minsAgo, dia, peak, profile);
                }

            }
            else
            { // empty return if (treatment.insulin) == False
                return null;
            }
        }

        static IOBCalc iobCalcBilinear(TempBolus treatment, double minsAgo, double dia)
        {
            var default_dia = 3.0; // assumed duration of insulin activity, in hours
            var peak = 75;        // assumed peak insulin activity, in minutes
            var end = 180;        // assumed end of insulin activity, in minutes

            // Scale minsAgo by the ratio of the default dia / the user's dia 
            // so the calculations for activityContrib and iobContrib work for 
            // other dia values (while using the constants specified above)
            var timeScalar = default_dia / dia;
            var scaled_minsAgo = timeScalar * minsAgo;


            double activityContrib = 0;
            double iobContrib = 0;

            // Calc percent of insulin activity at peak, and slopes up to and down from peak
            // Based on area of triangle, because area under the insulin action "curve" must sum to 1
            // (length * height) / 2 = area of triangle (1), therefore height (activityPeak) = 2 / length (which in this case is dia, in minutes)
            // activityPeak scales based on user's dia even though peak and end remain fixed
            var activityPeak = 2 / (dia * 60);
            var slopeUp = activityPeak / peak;
            var slopeDown = -1 * (activityPeak / (end - peak));

            if (scaled_minsAgo < peak)
            {

                activityContrib = treatment.insulin.Value * (slopeUp * scaled_minsAgo);

                var x1 = (scaled_minsAgo / 5) + 1;  // scaled minutes since bolus, pre-peak; divided by 5 to work with coefficients estimated based on 5 minute increments
                iobContrib = treatment.insulin.Value * ((-0.001852 * x1 * x1) + (0.001852 * x1) + 1.000000);

            }
            else if (scaled_minsAgo < end)
            {

                var minsPastPeak = scaled_minsAgo - peak;
                activityContrib = treatment.insulin.Value * (activityPeak + (slopeDown * minsPastPeak));

                var x2 = ((scaled_minsAgo - peak) / 5);  // scaled minutes past peak; divided by 5 to work with coefficients estimated based on 5 minute increments
                iobContrib = treatment.insulin.Value * ((0.001323 * x2 * x2) + (-0.054233 * x2) + 0.555560);
            }

            return new IOBCalc
            {
                activityContrib = activityContrib,
                iobContrib = iobContrib
            };
        }

        static IOBCalc iobCalcExponential(TempBolus treatment, double minsAgo, double dia, int peak, Profile profile)
        {
            // Use custom peak time (in minutes) if value is valid
            if (profile.curve == "rapid-acting")
            {
                if (profile.useCustomPeakTime == true && profile.insulinPeakTime != null)
                {
                    if (profile.insulinPeakTime > 120)
                    {
                        Console.WriteLine("Setting maximum Insulin Peak Time of 120m for " + profile.curve + " insulin");
                        peak = 120;
                    }
                    else if (profile.insulinPeakTime < 50)
                    {
                        Console.WriteLine("Setting minimum Insulin Peak Time of 50m for " + profile.curve + " insulin");
                        peak = 50;
                    }
                    else
                    {
                        peak = profile.insulinPeakTime.Value;
                    }
                }
                else
                {
                    peak = 75;
                }
            }
            else if (profile.curve == "ultra-rapid")
            {
                if (profile.useCustomPeakTime == true && profile.insulinPeakTime != null)
                {
                    if (profile.insulinPeakTime > 100)
                    {
                        Console.WriteLine("Setting maximum Insulin Peak Time of 100m for " + profile.curve + " insulin");
                        peak = 100;
                    }
                    else if (profile.insulinPeakTime < 35)
                    {
                        Console.WriteLine("Setting minimum Insulin Peak Time of 35m for " + profile.curve + " insulin");
                        peak = 35;
                    }
                    else
                    {
                        peak = profile.insulinPeakTime.Value;
                    }
                }
                else
                {
                    peak = 55;
                }
            }
            else
            {
                Console.WriteLine("Curve of " + profile.curve + " is not supported.");
                throw new NotSupportedException("Curve of " + profile.curve + " is not supported.");
            }
            var end = dia * 60;  // end of insulin activity, in minutes


            double activityContrib = 0;
            double iobContrib = 0;

            if (minsAgo < end)
            {
                // Formula source: https://github.com/LoopKit/Loop/issues/388#issuecomment-317938473
                // Mapping of original source variable names to those used here:
                //   td = end
                //   tp = peak
                //   t  = minsAgo
                var tau = peak * (1 - peak / end) / (1 - 2 * peak / end);  // time constant of exponential decay
                var a = 2 * tau / end;                                     // rise time factor
                var S = 1 / (1 - a + (1 + a) * Math.Exp(-end / tau));      // auxiliary scale factor

                activityContrib = treatment.insulin.Value * (S / Math.Pow(tau, 2)) * minsAgo * (1 - minsAgo / end) * Math.Exp(-minsAgo / tau);
                iobContrib = treatment.insulin.Value * (1 - S * (1 - a) * ((Math.Pow(minsAgo, 2) / (tau * end * (1 - a)) - minsAgo / tau - 1) * Math.Exp(-minsAgo / tau) + 1));
                //Console.WriteLine('DIA: ' + dia + ' minsAgo: ' + minsAgo + ' end: ' + end + ' peak: ' + peak + ' tau: ' + tau + ' a: ' + a + ' S: ' + S + ' activityContrib: ' + activityContrib + ' iobContrib: ' + iobContrib);
            }

            return new IOBCalc
            {
                activityContrib = activityContrib,
                iobContrib = iobContrib
            };
        }

    }

    class IOBCalc
    {
        public double activityContrib { get; set; }
        public double iobContrib { get; set; }
    }

    class TotalIOB
    {
        public double iob { get; set; }
        public double activity { get; set; }
        public double basaliob { get; set; }
        public double bolusiob { get; set; }
        public double netbasalinsulin { get; set; }
        public double bolusinsulin { get; set; }
        public DateTimeOffset time { get; set; }
        public TotalIOB iobWithZeroTemp { get; set; }
        public long lastBolusTime { get; set; }
        public TempHistory lastTemp { get; set; }
    }
}
