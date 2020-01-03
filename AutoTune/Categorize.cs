using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AutoTune
{
    class Categorize
    {
        public List<MealInput> treatments { get; set; }
        public Profile profile { get; set; }
        public List<Treatment> pumpHistory { get; set; }
        public List<Glucose> glucose { get; set; }
        public List<Basalprofile> basalprofile { get; set; }
        public List<Basalprofile> pumpbasalprofile { get; set; }
        public bool categorize_uam_as_basal { get; set; }

        public static Categorized categorizeBGDatums(Categorize opts)
        {
            var treatments = opts.treatments;
            // this sorts the treatments collection in order.
            treatments.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
            var profileData = opts.profile;

            var glucoseData = new List<Glucose>();
            if (opts.glucose != null)
            {
                //var glucoseData = opts.glucose;
                glucoseData = opts.glucose.Select((obj) =>
                {
                    //Support the NS sgv field to avoid having to convert in a custom way
                    obj.glucose ??= obj.sgv;

                    if (obj.date > 0)
                    {
                        //obj.BGTime = new Date(obj.date);
                    }
                    else if (!String.IsNullOrEmpty(obj.displayTime))
                    {
                        // Attempt to get date from displayTime
                        obj.date = DateTimeOffset.Parse(obj.displayTime).ToUnixTimeMilliseconds();
                    }
                    else if (obj.dateString > DateTimeOffset.MinValue)
                    {
                        // Attempt to get date from dateString
                        obj.date = obj.dateString.ToUnixTimeMilliseconds();
                    }// else { console.error("Could not determine BG time"); }

                    if (!(obj.dateString > DateTimeOffset.MinValue))
                    {
                        obj.dateString = DateTimeOffset.FromUnixTimeMilliseconds(obj.date).ToLocalTime();
                    }
                    return obj;
                }).Where(obj => obj.date > 0 && obj.glucose >= 39) // Only take records with a valid date record and a glucose value, which is also above 39
                .OrderByDescending(x => x.date)
                .ToList();
            }
            // if (typeof(opts.preppedGlucose) != "undefined") {
            // var preppedGlucoseData = opts.preppedGlucose;
            // }
            //starting variable at 0
            var boluses = 0;
            var maxCarbs = 0;
            //console.error(treatments);
            if (treatments.Count == 0) return null;

            //console.error(glucoseData);
            var IOBInputs = new FindInsulin
            {
                profile = profileData,
                history = opts.pumpHistory
            };
            var CSFGlucoseData = new List<Glucose>();
            var ISFGlucoseData = new List<Glucose>();
            var basalGlucoseData = new List<Glucose>();
            var UAMGlucoseData = new List<Glucose>();
            var CRData = new List<CRDatum>();

            var bucketedData = new Glucose[glucoseData.Count];
            if (glucoseData.Count > 0)
            {
                bucketedData[0] = JsonConvert.DeserializeObject<Glucose>(JsonConvert.SerializeObject(glucoseData[0]));
            }
            var j = 0;
            var k = 0; // index of first value used by bucket
                       //for loop to validate and bucket the data
            for (var i = 1; i < glucoseData.Count; ++i)
            {
                var BGTime = glucoseData[i].date;
                var lastBGTime = glucoseData[k].date;
                var elapsedMinutes = (BGTime - lastBGTime) / (60 * 1000);

                if (Math.Abs(elapsedMinutes) >= 2)
                {
                    j++; // move to next bucket
                    k = i; // store index of first value used by bucket
                    bucketedData[j] = JsonConvert.DeserializeObject<Glucose>(JsonConvert.SerializeObject(glucoseData[i]));
                }
                else
                {
                    // average all readings within time deadband
                    var glucoseTotal = glucoseData.Skip(k).Take(k - (i + 1)).Sum(x => x.glucose.Value);
                    bucketedData[j].glucose = glucoseTotal / (i - k + 1);
                }
            }
            //console.error(bucketedData);
            //console.error(bucketedData[bucketedData.length-1]);

            //console.error(treatments);
            // go through the treatments and remove any that are older than the oldest glucose value
            for (var i = treatments.Count - 1; i > 0; --i)
            {
                var treatment = treatments[i];
                //console.error(treatment);
                if (treatment != null)
                {
                    var treatmentDate = treatment.timestamp;
                    var treatmentTime = treatmentDate.ToUnixTimeMilliseconds();
                    var glucoseDatum = bucketedData[bucketedData.Length - 1];
                    //console.error(glucoseDatum);
                    if (glucoseDatum != null)
                    {
                        var BGTime = glucoseDatum.date;
                        if (treatmentTime < BGTime)
                        {
                            treatments.RemoveAt(i);
                        }
                    }
                }
            }
            //console.error(treatments);
            var calculatingCR = false;
            var absorbing = 0;
            var uam = 0; // unannounced meal
            double mealCOB = 0;
            var mealCarbs = 0;
            var CRCarbs = 0;
            var type = "";
            // main for loop
            var fullHistory = IOBInputs.history;
            var isf = new ISF();
            // WTF: JS variables with insane "var" scoping rules
            double? CRInitialIOB = null;
            int? CRInitialBG = null;
            DateTimeOffset? CRInitialCarbTime = null;
            for (var i = bucketedData.Length - 5; i > 0; --i)
            {
                var glucoseDatum = bucketedData[i];
                //console.error(glucoseDatum);
                var BGDate = DateTimeOffset.FromUnixTimeMilliseconds(glucoseDatum.date).ToLocalTime();
                var BGTime = glucoseDatum.date;
                // As we're processing each data point, go through the treatment.carbs and see if any of them are older than
                // the current BG data point.  If so, add those carbs to COB.
                var myCarbs = 0;
                if (treatments.LastOrDefault() is { } treatment)
                {
                    var treatmentDate = treatment.timestamp;
                    var treatmentTime = treatmentDate.ToUnixTimeMilliseconds();
                    //console.error(treatmentDate);
                    if (treatmentTime < BGTime)
                    {
                        if (treatment.carbs >= 1)
                        {
                            mealCOB += treatment.carbs.Value;
                            mealCarbs += treatment.carbs.Value;
                            myCarbs = treatment.carbs.Value;
                        }
                        treatments.RemoveAt(treatments.Count - 1);
                    }
                }

                double BG;
                double avgDelta;
                // TODO: re-implement interpolation to avoid issues here with gaps
                // calculate avgDelta as last 4 datapoints to better catch more rises after COB hits zero
                if (bucketedData[i].glucose != null && bucketedData[i + 4].glucose != null)
                {
                    //console.error(bucketedData[i]);
                    BG = bucketedData[i].glucose.Value;
                    if (BG < 40 || bucketedData[i + 4].glucose < 40)
                    {
                        //process.stderr.write("!");
                        continue;
                    }
                    avgDelta = (BG - bucketedData[i + 4].glucose.Value) / 4;
                }
                else
                {
                    Console.WriteLine("Could not find glucose data");
                    Debug.Assert(false, "Could not find glucose data");
                    continue; // WTF: original code would just go on
                }

                avgDelta = JSMath.Round(avgDelta, 2);
                glucoseDatum.avgDelta = avgDelta;

                //sens = ISF
                var sens = isf.isfLookup(IOBInputs.profile.isfProfile, BGDate);
                IOBInputs.clock = BGDate;
                // trim down IOBInputs.history to just the data for 6h prior to BGDate
                //console.error(IOBInputs.history[0].created_at);
                var newHistory = new List<Treatment>();
                for (var h = 0; h < fullHistory.Count; h++)
                {
                    var hDate = fullHistory[h].created_at;
                    //console.error(fullHistory[i].created_at, hDate, BGDate, BGDate-hDate);
                    //if (h == 0 || h == fullHistory.length - 1) {
                    //console.error(hDate, BGDate, hDate-BGDate)
                    //}
                    if (BGDate - hDate < TimeSpan.FromHours(6) && BGDate - hDate > TimeSpan.Zero)
                    {
                        //process.stderr.write("i");
                        //console.error(hDate);
                        newHistory.Add(fullHistory[h]);
                    }
                }
                IOBInputs.history = newHistory;
                // process.stderr.write("" + newHistory.length + " ");
                //console.error(newHistory[0].created_at,newHistory[newHistory.length-1].created_at,newHistory.length);


                // for IOB calculations, use the average of the last 4 hours' basals to help convergence;
                // this helps since the basal this hour could be different from previous, especially if with autotune they start to diverge.
                // use the pumpbasalprofile to properly calculate IOB during periods where no temp basal is set
                var currentPumpBasal = basal.basalLookup(opts.pumpbasalprofile, BGDate);
                var BGDate1hAgo = DateTimeOffset.FromUnixTimeMilliseconds(BGTime - 1 * 60 * 60 * 1000).ToLocalTime();
                var BGDate2hAgo = DateTimeOffset.FromUnixTimeMilliseconds(BGTime - 2 * 60 * 60 * 1000).ToLocalTime();
                var BGDate3hAgo = DateTimeOffset.FromUnixTimeMilliseconds(BGTime - 3 * 60 * 60 * 1000).ToLocalTime();
                var basal1hAgo = basal.basalLookup(opts.pumpbasalprofile, BGDate1hAgo);
                var basal2hAgo = basal.basalLookup(opts.pumpbasalprofile, BGDate2hAgo);
                var basal3hAgo = basal.basalLookup(opts.pumpbasalprofile, BGDate3hAgo);
                var sum = currentPumpBasal + basal1hAgo + basal2hAgo + basal3hAgo;
                IOBInputs.profile.currentBasal = JSMath.Round((sum / 4), 3);

                // this is the current autotuned basal, used for everything else besides IOB calculations
                var currentBasal = basal.basalLookup(opts.basalprofile, BGDate);

                //console.error(currentBasal,basal1hAgo,basal2hAgo,basal3hAgo,IOBInputs.profile.currentBasal);
                // basalBGI is BGI of basal insulin activity.
                var basalBGI = JSMath.Round((currentBasal * sens / 60 * 5), 2); // U/hr * mg/dL/U * 1 hr / 60 minutes * 5 = mg/dL/5m
                                                                                //console.log(JSON.stringify(IOBInputs.profile));
                                                                                // call iob since calculated elsewhere
                var iob = IOB.getIOB(IOBInputs)[0]; // WTF: why doesn't this pass currentIOBOnly = true?
                //console.error(JSON.stringify(iob));

                // activity times ISF times 5 minutes is BGI
                var BGI = JSMath.Round((-iob.activity * sens * 5), 2);
                // datum = one glucose data point (being prepped to store in output)
                glucoseDatum.BGI = BGI;
                // calculating deviation
                var deviation = avgDelta - BGI;
                //console.error(deviation,avgDelta,BG,bucketedData[i].glucose);

                // set positive deviations to zero if BG is below 80
                if (BG < 80 && deviation > 0)
                {
                    deviation = 0;
                }

                // rounding and storing deviation
                deviation = JSMath.Round(deviation, 2);
                glucoseDatum.deviation = deviation;

                // Then, calculate carb absorption for that 5m interval using the deviation.
                if (mealCOB > 0)
                {
                    var profile = profileData;
                    var ci = Math.Max(deviation, profile.min_5m_carbimpact);
                    var absorbed = ci * profile.carb_ratio / sens;
                    // Store the COB, and use it as the starting point for the next data point.
                    mealCOB = Math.Max(0, mealCOB - absorbed);
                }

                // Calculate carb ratio (CR) independently of CSF and ISF
                // Use the time period from meal bolus/carbs until COB is zero and IOB is < currentBasal/2
                // For now, if another meal IOB/COB stacks on top of it, consider them together
                // Compare beginning and ending BGs, and calculate how much more/less insulin is needed to neutralize
                // Use entered carbs vs. starting IOB + delivered insulin + needed-at-end insulin to directly calculate CR.

                if (mealCOB > 0 || calculatingCR)
                {
                    // set initial values when we first see COB
                    CRCarbs += myCarbs;

                    if (!calculatingCR)
                    {
                        CRInitialIOB = iob.iob;
                        CRInitialBG = glucoseDatum.glucose;
                        CRInitialCarbTime = DateTimeOffset.FromUnixTimeMilliseconds(glucoseDatum.date).ToLocalTime();
                        Console.WriteLine($"CRInitialIOB: {CRInitialIOB} CRInitialBG: {CRInitialBG} CRInitialCarbTime: {CRInitialCarbTime.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}");
                    }

                    // keep calculatingCR as long as we have COB or enough IOB
                    if (mealCOB > 0 && i > 1)
                    {
                        calculatingCR = true;
                    }
                    else if (iob.iob > currentBasal / 2 && i > 1)
                    {
                        calculatingCR = true;
                        // when COB=0 and IOB drops low enough, record end values and be done calculatingCR
                    }
                    else
                    {
                        var CREndIOB = iob.iob;
                        var CREndBG = glucoseDatum.glucose;
                        var CREndTime = DateTimeOffset.FromUnixTimeMilliseconds(glucoseDatum.date).ToLocalTime();
                        Console.WriteLine($"CREndIOB: {CREndIOB} CREndBG: {CREndBG} CREndTime: {CREndTime.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}");
                        var CRDatum = new CRDatum
                        {
                            CRInitialIOB = CRInitialIOB,
                            CRInitialBG = CRInitialBG,
                            CRInitialCarbTime = CRInitialCarbTime,
                            CREndIOB = CREndIOB,
                            CREndBG = CREndBG,
                            CREndTime = CREndTime,
                            CRCarbs = CRCarbs
                        };
                        //console.error(CRDatum);

                        var CRElapsedMinutes = JSMath.Round((CREndTime - CRInitialCarbTime.Value).TotalMinutes);
                        //console.error(CREndTime - CRInitialCarbTime, CRElapsedMinutes);
                        if (CRElapsedMinutes < 60 || (i == 1 && mealCOB > 0))
                        {
                            Console.WriteLine("Ignoring " + CRElapsedMinutes + " m CR period.");
                        }
                        else
                        {
                            CRData.Add(CRDatum);
                        }

                        CRCarbs = 0;
                        calculatingCR = false;
                    }
                }


                // If mealCOB is zero but all deviations since hitting COB=0 are positive, assign those data points to CSFGlucoseData
                // Once deviations go negative for at least one data point after COB=0, we can use the rest of the data to tune ISF or basals
                if (mealCOB > 0 || absorbing > 0 || mealCarbs > 0)
                {
                    // if meal IOB has decayed, then end absorption after this data point unless COB > 0
                    if (iob.iob < currentBasal / 2)
                    {
                        absorbing = 0;
                        // otherwise, as long as deviations are positive, keep tracking carb deviations
                    }
                    else if (deviation > 0)
                    {
                        absorbing = 1;
                    }
                    else
                    {
                        absorbing = 0;
                    }
                    if (absorbing == 0 && mealCOB == 0)
                    {
                        mealCarbs = 0;
                    }
                    // check previous "type" value, and if it wasn't csf, set a mealAbsorption start flag
                    //console.error(type);
                    if (type != "csf")
                    {
                        glucoseDatum.mealAbsorption = "start";
                        Console.WriteLine(glucoseDatum.mealAbsorption + " carb absorption");
                    }
                    type = "csf";
                    glucoseDatum.mealCarbs = mealCarbs;
                    //if (i == 0) { glucoseDatum.mealAbsorption = "end"; }
                    CSFGlucoseData.Add(glucoseDatum);
                }
                else
                {
                    // check previous "type" value, and if it was csf, set a mealAbsorption end flag
                    if (type == "csf")
                    {
                        CSFGlucoseData[CSFGlucoseData.Count - 1].mealAbsorption = "end";
                        Console.WriteLine(CSFGlucoseData[CSFGlucoseData.Count - 1].mealAbsorption + " carb absorption");
                    }

                    if (iob.iob > 2 * currentBasal || deviation > 6 || uam > 0)
                    {
                        if (deviation > 0)
                        {
                            uam = 1;
                        }
                        else
                        {
                            uam = 0;
                        }
                        if (type != "uam")
                        {
                            glucoseDatum.uamAbsorption = "start";
                            Console.WriteLine(glucoseDatum.uamAbsorption + " uannnounced meal absorption");
                        }
                        type = "uam";
                        UAMGlucoseData.Add(glucoseDatum);
                    }
                    else
                    {
                        if (type == "uam")
                        {
                            Console.WriteLine("end unannounced meal absorption");
                        }

                        // Go through the remaining time periods and divide them into periods where scheduled basal insulin activity dominates. This would be determined by calculating the BG impact of scheduled basal insulin (for example 1U/hr * 48 mg/dL/U ISF = 48 mg/dL/hr = 5 mg/dL/5m), and comparing that to BGI from bolus and net basal insulin activity.
                        // When BGI is positive (insulin activity is negative), we want to use that data to tune basals
                        // When BGI is smaller than about 1/4 of basalBGI, we want to use that data to tune basals
                        // When BGI is negative and more than about 1/4 of basalBGI, we can use that data to tune ISF,
                        // unless avgDelta is positive: then that's some sort of unexplained rise we don't want to use for ISF, so that means basals
                        if (basalBGI > -4 * BGI)
                        {
                            type = "basal";
                            basalGlucoseData.Add(glucoseDatum);
                        }
                        else
                        {
                            if (avgDelta > 0 && avgDelta > -2 * BGI)
                            {
                                //type="unknown"
                                type = "basal";
                                basalGlucoseData.Add(glucoseDatum);
                            }
                            else
                            {
                                type = "ISF";
                                ISFGlucoseData.Add(glucoseDatum);
                            }
                        }
                    }
                }

                // debug line to print out all the things
                decimal R(double value, int digits) => JSMath.ToFixed(value, digits);
                Console.WriteLine($"{absorbing} mealCOB: {R(mealCOB, 1):F1} mealCarbs: {mealCarbs} basalBGI: {R(basalBGI, 1):F1} BGI: {R(BGI, 1):F1} IOB: {R(iob.iob, 1):F1} at {BGDate.ToLocalTime():HH:mm:ss} dev: {R(deviation, 2):F2} avgDelta: {R(avgDelta, 2):F2} {type}");
            }

            IOBInputs = new FindInsulin
            {
                profile = profileData,
                history = opts.pumpHistory
            };
            var stuff = History.find_insulin(IOBInputs);
            foreach (var CRDatum in CRData)
            {
                CRDatum.CRInsulin = insulinDosed(stuff, opts.profile, CRDatum.CRInitialCarbTime.Value, CRDatum.CREndTime);

                //console.error(CRDatum);
            }

            var CSFLength = CSFGlucoseData.Count;
            var ISFLength = ISFGlucoseData.Count;
            var UAMLength = UAMGlucoseData.Count;
            var basalLength = basalGlucoseData.Count;

            if (opts.categorize_uam_as_basal)
            {
                Console.WriteLine("--categorize-uam-as-basal=true set: categorizing all UAM data as basal.");
                basalGlucoseData.AddRange(UAMGlucoseData);
            }
            else if (CSFLength > 12)
            {
                Console.WriteLine("Found at least 1h of carb absorption: assuming all meals were announced, and categorizing UAM data as basal.");
                basalGlucoseData.AddRange(UAMGlucoseData);
            }
            else
            {
                if (2 * basalLength < UAMLength)
                {
                    //console.error(basalGlucoseData, UAMGlucoseData);
                    Console.WriteLine("Warning: too many deviations categorized as UnAnnounced Meals");
                    Console.WriteLine("Adding " + UAMLength + " UAM deviations to " + basalLength + " basal ones");
                    basalGlucoseData.AddRange(UAMGlucoseData);
                    //console.error(basalGlucoseData);
                    // if too much data is excluded as UAM, add in the UAM deviations to basal, but then discard the highest 50%
                    basalGlucoseData.Sort((a, b) => a.deviation.CompareTo(b.deviation));
                    var newBasalGlucose = basalGlucoseData.GetRange(0, basalGlucoseData.Count / 2);
                    //console.error(newBasalGlucose);
                    basalGlucoseData = newBasalGlucose;
                    Console.WriteLine("and selecting the lowest 50%, leaving " + basalGlucoseData.Count + " basal+UAM ones");
                }

                if (2 * ISFLength < UAMLength)
                {
                    Console.WriteLine("Adding " + UAMLength + " UAM deviations to " + ISFLength + " ISF ones");
                    ISFGlucoseData.AddRange(UAMGlucoseData);
                    // if too much data is excluded as UAM, add in the UAM deviations to ISF, but then discard the highest 50%
                    ISFGlucoseData.Sort((a, b) => a.deviation.CompareTo(b.deviation));
                    var newISFGlucose = ISFGlucoseData.GetRange(0, ISFGlucoseData.Count / 2);
                    //console.error(newISFGlucose);
                    ISFGlucoseData = newISFGlucose;
                    Console.WriteLine("and selecting the lowest 50%, leaving " + ISFGlucoseData.Count + " ISF+UAM ones");
                    //console.error(ISFGlucoseData.length, UAMLength);
                }
            }
            basalLength = basalGlucoseData.Count;
            ISFLength = ISFGlucoseData.Count;
            if (4 * basalLength + ISFLength < CSFLength && ISFLength < 10)
            {
                Console.WriteLine("Warning: too many deviations categorized as meals");
                //console.error("Adding",CSFLength,"CSF deviations to",basalLength,"basal ones");
                //var basalGlucoseData = basalGlucoseData.concat(CSFGlucoseData);
                Console.WriteLine($"Adding {CSFLength} CSF deviations to {ISFLength} ISF ones");
                ISFGlucoseData.AddRange(CSFGlucoseData);
                CSFGlucoseData = new List<Glucose>();
            }

            return new Categorized
            {
                CRData = CRData,
                CSFGlucoseData = CSFGlucoseData,
                ISFGlucoseData = ISFGlucoseData,
                basalGlucoseData = basalGlucoseData
            };
        }

        static double? insulinDosed(List<TempData> treatments, Profile profile_data, DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var start = startDate.ToUnixTimeMilliseconds();
            var end = endDate.ToUnixTimeMilliseconds();
            double insulinDosed = 0;
            if (treatments == null)
            {
                Console.WriteLine("No treatments to process.");
                return null;
            }

            foreach (var treatment in treatments.OfType<TempBolus>())
            {
                //console.error(treatment);
                if (treatment.insulin.HasValue && treatment.date > start && treatment.date <= end) // WTF: TempBolus without insulin
                {
                    insulinDosed += treatment.insulin.Value;
                }
            }
            //console.error(insulinDosed);

            return JSMath.Round(insulinDosed, 3);
        }
    }

    class Categorized
    {
        public List<CRDatum> CRData { get; set; }
        public List<Glucose> CSFGlucoseData { get; set; }
        public List<Glucose> ISFGlucoseData { get; set; }
        public List<Glucose> basalGlucoseData { get; set; }

        //autotune output
        public List<DIADeviation> diaDeviations { get; set; }
        public List<PeakDeviation> peakDeviations { get; set; }
    }

    class CRDatum
    {
        public double? CRInitialIOB { get; set; }
        public int? CRInitialBG { get; set; }
        public DateTimeOffset? CRInitialCarbTime { get; set; }
        public double CREndIOB { get; set; }
        public int? CREndBG { get; set; }
        public DateTimeOffset CREndTime { get; set; }
        public int CRCarbs { get; set; }
        public double? CRInsulin { get; set; }

        // autotune core calculated
        public double? CRInsulinTotal { get; set; }
    }
}