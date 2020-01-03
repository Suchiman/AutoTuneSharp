using System;
using System.Collections.Generic;

namespace AutoTune
{
    static class AutotunePrep
    {
        public static Categorized generate(Prepare inputs)
        {
            //console.error(inputs);
            var treatments = Meal.findMealInputs(inputs);

            var opts = new Categorize
            {
                treatments = treatments,
                profile = inputs.profile,
                pumpHistory = inputs.history,
                glucose = inputs.glucose,
                //prepped_glucose = inputs.prepped_glucose,
                basalprofile = inputs.profile.basalprofile,
                pumpbasalprofile = inputs.pumpprofile.basalprofile,
                categorize_uam_as_basal = inputs.categorize_uam_as_basal
            };

            var autotune_prep_output = Categorize.categorizeBGDatums(opts);

            if (inputs.tune_insulin_curve)
            {
                if (opts.profile.curve == "bilinear")
                {
                    Console.WriteLine("--tune-insulin-curve is set but only valid for exponential curves");
                }
                else
                {
                    double minDeviations = 1000000;
                    double newDIA = 0;
                    var diaDeviations = new List<DIADeviation>();
                    var peakDeviations = new List<PeakDeviation>();
                    var currentDIA = opts.profile.dia;
                    var currentPeak = opts.profile.insulinPeakTime;

                    Action<string> consoleError = Console.WriteLine;
                    //console.error = function() {};

                    double startDIA = currentDIA - 2;
                    double endDIA = currentDIA + 2;
                    for (var dia = startDIA; dia <= endDIA; ++dia)
                    {
                        double sqrtDeviations = 0;
                        double deviations = 0;
                        double deviationsSq = 0;

                        opts.profile.dia = dia;

                        var curve_output = Categorize.categorizeBGDatums(opts);
                        var basalGlucose = curve_output.basalGlucoseData;

                        for (int hour = 0; hour < 24; ++hour)
                        {
                            for (int i = 0; i < basalGlucose.Count; ++i)
                            {
                                DateTimeOffset BGTime;

                                if (basalGlucose[i].date > 0)
                                {
                                    BGTime = DateTimeOffset.FromUnixTimeMilliseconds(basalGlucose[i].date).ToLocalTime();
                                }
                                else if (!String.IsNullOrEmpty(basalGlucose[i].displayTime))
                                {
                                    BGTime = DateTimeOffset.Parse(basalGlucose[i].displayTime).ToLocalTime();
                                }
                                else if (basalGlucose[i].dateString > DateTimeOffset.MinValue)
                                {
                                    BGTime = basalGlucose[i].dateString.ToLocalTime();
                                }
                                else
                                {
                                    consoleError("Could not determine last BG time");
                                    throw new Exception("Could not determine last BG time");
                                }

                                var myHour = BGTime.Hour;
                                if (hour == myHour)
                                {
                                    //console.error(basalGlucose[i].deviation);
                                    sqrtDeviations += Math.Pow(Math.Abs(basalGlucose[i].deviation), 0.5);
                                    deviations += Math.Abs(basalGlucose[i].deviation);
                                    deviationsSq += Math.Pow(basalGlucose[i].deviation, 2);
                                }
                            }
                        }

                        var meanDeviation = JSMath.Round(Math.Abs(deviations / basalGlucose.Count), 3);
                        var SMRDeviation = JSMath.Round(Math.Pow(sqrtDeviations / basalGlucose.Count, 2), 3);
                        var RMSDeviation = JSMath.Round(Math.Pow(deviationsSq / basalGlucose.Count, 0.5), 3);
                        consoleError($"insulinEndTime {dia} meanDeviation: {meanDeviation} SMRDeviation: {SMRDeviation} RMSDeviation: {RMSDeviation} (mg/dL)");
                        diaDeviations.Add(new DIADeviation
                        {
                            dia = dia,
                            meanDeviation = meanDeviation,
                            SMRDeviation = SMRDeviation,
                            RMSDeviation = RMSDeviation,
                        });
                        autotune_prep_output.diaDeviations = diaDeviations;

                        deviations = JSMath.Round(deviations, 3);
                        if (deviations < minDeviations)
                        {
                            minDeviations = JSMath.Round(deviations, 3);
                            newDIA = dia;
                        }
                    }

                    // consoleError('Optimum insulinEndTime', newDIA, 'mean deviation:', JSMath.Round(minDeviations/basalGlucose.length*1000)/1000, '(mg/dL)');
                    //consoleError(diaDeviations);

                    minDeviations = 1000000;

                    var newPeak = 0;
                    opts.profile.dia = currentDIA;
                    //consoleError(opts.profile.useCustomPeakTime, opts.profile.insulinPeakTime);
                    if (!opts.profile.useCustomPeakTime == true && opts.profile.curve == "ultra-rapid")
                    {
                        opts.profile.insulinPeakTime = 55;
                    }
                    else if (!opts.profile.useCustomPeakTime == true)
                    {
                        opts.profile.insulinPeakTime = 75;
                    }
                    opts.profile.useCustomPeakTime = true;

                    var startPeak = opts.profile.insulinPeakTime.Value - 10;
                    var endPeak = opts.profile.insulinPeakTime.Value + 10;
                    for (var peak = startPeak; peak <= endPeak; peak = (peak + 5))
                    {
                        double sqrtDeviations = 0;
                        double deviations = 0;
                        double deviationsSq = 0;

                        opts.profile.insulinPeakTime = peak;

                        var curve_output = Categorize.categorizeBGDatums(opts);
                        var basalGlucose = curve_output.basalGlucoseData;

                        for (int hour = 0; hour < 24; ++hour)
                        {
                            for (int i = 0; i < basalGlucose.Count; ++i)
                            {
                                DateTimeOffset BGTime;
                                if (basalGlucose[i].date > 0)
                                {
                                    BGTime = DateTimeOffset.FromUnixTimeMilliseconds(basalGlucose[i].date).ToLocalTime();
                                }
                                else if (!String.IsNullOrEmpty( basalGlucose[i].displayTime))
                                {
                                    BGTime = DateTimeOffset.Parse(basalGlucose[i].displayTime).ToLocalTime();
                                }
                                else if (basalGlucose[i].dateString > DateTimeOffset.MinValue)
                                {
                                    BGTime = basalGlucose[i].dateString.ToLocalTime();
                                }
                                else
                                {
                                    consoleError("Could not determine last BG time");
                                    throw new Exception("Could not determine last BG time");
                                }

                                var myHour = BGTime.Hour;
                                if (hour == myHour)
                                {
                                    //console.error(basalGlucose[i].deviation);
                                    sqrtDeviations += Math.Pow(Math.Abs(basalGlucose[i].deviation), 0.5);
                                    deviations += Math.Abs(basalGlucose[i].deviation);
                                    deviationsSq += Math.Pow(basalGlucose[i].deviation, 2);
                                }
                            }
                        }
                        Console.WriteLine(deviationsSq);

                        double meanDeviation = JSMath.Round(deviations / basalGlucose.Count, 3);
                        double SMRDeviation = JSMath.Round(Math.Pow(sqrtDeviations / basalGlucose.Count, 2), 3);
                        double RMSDeviation = JSMath.Round(Math.Pow(deviationsSq / basalGlucose.Count, 0.5), 3);
                        consoleError($"insulinPeakTime {peak} meanDeviation: {meanDeviation} SMRDeviation: {SMRDeviation} RMSDeviation: {RMSDeviation} (mg/dL)");
                        peakDeviations.Add(new PeakDeviation
                        {
                            peak = peak,
                            meanDeviation = meanDeviation,
                            SMRDeviation = SMRDeviation,
                            RMSDeviation = RMSDeviation,
                        });
                        autotune_prep_output.diaDeviations = diaDeviations;

                        deviations = JSMath.Round(deviations, 3);
                        if (deviations < minDeviations)
                        {
                            minDeviations = JSMath.Round(deviations, 3);
                            newPeak = peak;
                        }
                    }

                    //consoleError($"Optimum insulinPeakTime {newPeak} mean deviation: {JSMath.Round(minDeviations/basalGlucose.Count, 3)} (mg/dL)");
                    //consoleError(peakDeviations);
                    autotune_prep_output.peakDeviations = peakDeviations;

                    //console.error = consoleError;
                }
            }

            return autotune_prep_output;
        }
    }

    class PeakDeviation
    {
        public int peak { get; set; }
        public double meanDeviation { get; set; }
        public double SMRDeviation { get; set; }
        public double RMSDeviation { get; set; }
    }

    class DIADeviation
    {
        public double dia { get; set; }
        public double meanDeviation { get; set; }
        public double SMRDeviation { get; set; }
        public double RMSDeviation { get; set; }
    }
}
