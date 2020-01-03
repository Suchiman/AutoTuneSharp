using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AutoTune
{
    /// <summary>
    /// https://androidaps-altguide.readthedocs.io/en/latest/pages/Autotune.html
    /// Another point to note is how temp basal records are recorded in Nightscout.
    /// If you record temp basals as a percentage of the profile basal,
    /// those records will not be used by Autotune and it will assume that the profile basal was in force at the time.
    /// Your temp basal records should either be recorded as absolute basal values,
    /// or be percentages but with the additional rate property specified as part of the record as well.
    /// </summary>
    class Program
    {
        static async Task Main(DirectoryInfo dir, string nsHost, int startDaysAgo = 1, int endDaysAgo = 1, string startDate = null, string endDate = null, bool log = true, bool categorizeUamAsBasal = false, bool tuneInsulinCurve = false)
        {
            CultureInfo info = (CultureInfo)CultureInfo.CurrentCulture.Clone();
            info.NumberFormat.NumberDecimalSeparator = ".";
            CultureInfo.CurrentCulture = info;
            if (String.IsNullOrWhiteSpace(startDate))
            {
                startDate = DateTime.Now.AddDays(-startDaysAgo).ToString("yyyy-MM-dd");
            }
            if (String.IsNullOrWhiteSpace(endDate))
            {
                endDate = DateTime.Now.AddDays(-endDaysAgo).ToString("yyyy-MM-dd");
            }
            var autotune = dir.CreateSubdirectory("autotune");
            var settings = dir.CreateSubdirectory("settings");
            string Autotune(string filename) => Path.Combine(autotune.FullName, filename);
            string Settings(string filename) => Path.Combine(settings.FullName, filename);
            bool TryCopyHard(string source, string destination, bool overwrite = true, bool fail = true)
            {
                try
                {
                    if (!overwrite && File.Exists(destination))
                    {
                        return true;
                    }

                    File.Copy(source, destination, overwrite);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cannot copy {source} to {destination}: {ex.Message}");
                    if (fail)
                    {
                        Environment.Exit(-1);
                    }
                    return false;
                }
            }

            TryCopyHard(Settings("pumpprofile.json"), Autotune("profile.pump.json"));

            // This allows manual users to be able to run autotune by simply creating a settings/pumpprofile.json file.
            TryCopyHard(Settings("pumpprofile.json"), Settings("profile.json"), overwrite: false);

            // If a previous valid settings/autotune.json exists, use that; otherwise start from settings/profile.json
            if (!TryCopyHard(Settings("autotune.json"), Autotune("profile.json"), fail: false))
            {
                TryCopyHard(Autotune("profile.pump.json"), Autotune("profile.json"), fail: false);
            }

            // Build date list for autotune iteration
            var dateList = new List<DateTime>();
            var date = DateTime.Parse(startDate);
            var endDateP = DateTime.Parse(endDate);
            do
            {
                dateList.Add(date);
                date = date.AddDays(1);
            }
            while (date <= endDateP);

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(nsHost);
            //client.DefaultRequestHeaders.Add("api-secret", value);

            // Get Nightscout BG (sgv.json) Entries
            foreach (DateTime i in dateList)
            {
                DateTimeOffset d = i;
                string query = $"find%5Bdate%5D%5B%24gte%5D={d.AddHours(4).ToUnixTimeMilliseconds()}&find%5Bdate%5D%5B%24lte%5D={d.AddHours(28).ToUnixTimeMilliseconds()}&count=1000"; // 1500
                Console.WriteLine($"Query: {nsHost.TrimEnd('/')} entries/sgv.json {query}");
                var resp = await client.GetAsync("/api/v1/entries/sgv.json?" + query);
                var strResp = await resp.Content.ReadAsStringAsync();
                File.WriteAllText(Autotune($"ns-entries.{d:yyyy-MM-dd}.json"), strResp);

                string query2 = $"find%5Bcreated_at%5D%5B%24gte%5D={d.AddHours(-18):yyyy-MM-ddTHH:mmzzz}&find%5Bcreated_at%5D%5B%24lte%5D={d.AddHours(42):yyyy-MM-ddTHH:mmzzz}";
                Console.WriteLine($"Query: {nsHost.TrimEnd('/')} treatments.json {query2}");
                var resp2 = await client.GetAsync("/api/v1/treatments.json?" + query2);
                var strResp2 = await resp2.Content.ReadAsStringAsync();
                File.WriteAllText(Autotune($"ns-treatments.{d:yyyy-MM-dd}.json"), strResp2);

                if (strResp == "[]")
                {
                    continue;
                }

                try
                {
                    Console.WriteLine($"oref0-autotune-prep {(categorizeUamAsBasal ? "--categorize_uam_as_basal" : "")} {(tuneInsulinCurve ? "--tune-insulin-curve" : "")} ns-treatments.{d:yyyy-MM-dd}.json profile.json ns-entries.{d:yyyy-MM-dd}.json profile.pump.json > autotune.{d:yyyy-MM-dd}.json");
                    var prep = DoAutotunePrepare(Autotune($"ns-treatments.{d:yyyy-MM-dd}.json"), Autotune("profile.json"), Autotune($"ns-entries.{d:yyyy-MM-dd}.json"), Autotune("profile.pump.json"), null, categorizeUamAsBasal, tuneInsulinCurve);
                    var json = JsonConvert.SerializeObject(prep, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                    File.WriteAllText(Autotune($"autotune.{d:yyyy-MM-dd}.json"), json);

                    Console.WriteLine($"oref0-autotune-core autotune.{d:yyyy-MM-dd}.json profile.json profile.pump.json > newprofile.{d:yyyy-MM-dd}.json");
                    var tuned = DoAutotuneCore(Autotune($"autotune.{d:yyyy-MM-dd}.json"), Autotune("profile.json"), Autotune("profile.pump.json"), prep);
                    var tunedJson = JsonConvert.SerializeObject(tuned);
                    File.WriteAllText(Autotune($"newprofile.{d:yyyy-MM-dd}.json"), tunedJson);

                    TryCopyHard(Autotune($"newprofile.{d:yyyy-MM-dd}.json"), Autotune("profile.json"));

                    string report = GenerateReport(Autotune("profile.pump.json"), Autotune("profile.json"));
                    string report_file = Autotune("autotune_recommendations.log");
                    Console.WriteLine();
                    Console.WriteLine("Autotune pump profile recommendations:");
                    Console.WriteLine("---------------------------------------------------------");
                    Console.WriteLine("Recommendations Log File: " + report_file);
                    Console.WriteLine();
                    Console.Write(report);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    return;
                }
            }
        }

        static Categorized DoAutotunePrepare(string pumphistory_input, string profile_input, string glucose_input, string pumpprofile_input, string carb_input, bool categorize_uam_as_basal, bool tune_insulin_curve)
        {
            //bool categorize_uam_as_basal = false; // [--categorize_uam_as_basal]
            //bool tune_insulin_curve = false; // [--tune-insulin-curve]

            //if (inputs.Length < 4 || inputs.Length > 5)
            //{
            //    Console.WriteLine("<pumphistory.json> <profile.json> <glucose.json> <pumpprofile.json> [<carbhistory.json>] [--categorize_uam_as_basal] [--tune-insulin-curve]");
            //    Console.WriteLine("{ \"error\": \"Insufficient arguments\" }");
            //    Environment.Exit(1);
            //}

            //var pumphistory_input = inputs[0]; // <pumphistory.json>
            //var profile_input = inputs[1]; // <profile.json>
            //var glucose_input = inputs[2]; // <glucose.json>
            //var pumpprofile_input = inputs[3]; // <pumpprofile.json>
            //var carb_input = inputs[4]; // [<carbhistory.json>]

            List<Treatment> pumphistory_data;
            Profile profile_data;
            try
            {
                pumphistory_data = JsonConvert.DeserializeObject<List<Treatment>>(File.ReadAllText(pumphistory_input));
                profile_data = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(profile_input));
            }
            catch (Exception e)
            {
                Console.WriteLine("{ \"error\": \"Could not parse input data\" }");
                Console.WriteLine("Could not parse input data: " + e);
                return null;
            }

            Profile pumpprofile_data = null;
            if (pumpprofile_input != null)
            {
                try
                {
                    pumpprofile_data = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(pumpprofile_input));
                }
                catch (Exception e)
                {
                    Console.WriteLine("Warning: could not parse " + pumpprofile_input);
                    return null;
                }
            }

            // disallow impossibly low carbRatios due to bad decoding
            if (profile_data.carb_ratio < 2)
            {
                if (pumpprofile_data == null || pumpprofile_data.carb_ratio < 2)
                {
                    Console.WriteLine("{ \"carbs\": 0, \"mealCOB\": 0, \"reason\": \"carb_ratios " + profile_data.carb_ratio + " and " + pumpprofile_data?.carb_ratio + " out of bounds\" }");
                    Console.WriteLine("Error: carb_ratios " + profile_data.carb_ratio + " and " + pumpprofile_data?.carb_ratio + " out of bounds");
                    return null;
                }
                else
                {
                    profile_data.carb_ratio = pumpprofile_data.carb_ratio;
                }
            }

            // get insulin curve from pump profile that is maintained
            profile_data.curve = pumpprofile_data.curve;

            // Pump profile has an up to date copy of useCustomPeakTime from preferences
            // If the preferences file has useCustomPeakTime use the previous autotune dia and PeakTime.
            // Otherwise, use data from pump profile.
            if (!pumpprofile_data.useCustomPeakTime)
            {
                profile_data.dia = pumpprofile_data.dia;
                profile_data.insulinPeakTime = pumpprofile_data.insulinPeakTime;
            }

            // Always keep the curve value up to date with what's in the user preferences
            profile_data.curve = pumpprofile_data.curve;

            List<Glucose> glucose_data;
            try
            {
                glucose_data = JsonConvert.DeserializeObject<List<Glucose>>(File.ReadAllText(glucose_input));
            }
            catch (Exception e)
            {
                Console.WriteLine("Warning: could not parse " + glucose_input);
                return null;
            }

            List<object> carb_data = null;
            if (carb_input != null)
            {
                try
                {
                    carb_data = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(carb_input));
                }
                catch (Exception e)
                {
                    Console.WriteLine("Warning: could not parse " + carb_input);
                    return null;
                }
            }

            // Have to sort history - NS sort doesn't account for different zulu and local timestamps
            pumphistory_data = pumphistory_data.OrderByDescending(x => x.created_at).ToList();

            var prep = new Prepare
            {
                history = pumphistory_data,
                profile = profile_data,
                pumpprofile = pumpprofile_data,
                carbs = carb_data,
                glucose = glucose_data,
                categorize_uam_as_basal = categorize_uam_as_basal,
                tune_insulin_curve = tune_insulin_curve
            };

            var prepped_glucose = AutotunePrep.generate(prep);
            return prepped_glucose;
        }

        static Profile DoAutotuneCore(string prepped_glucose_input, string previous_autotune_input, string pumpprofile_input, Categorized prepped_glucose = null)
        {
            //if (inputs.Length != 3)
            //{
            //    Console.WriteLine("<autotune/glucose.json> <autotune/autotune.json> <settings/profile.json>");
            //    Environment.Exit(1);
            //}

            //string prepped_glucose_input = inputs[0];
            //string previous_autotune_input = inputs[1];
            //string pumpprofile_input = inputs[2];

            Categorized prepped_glucose_data = prepped_glucose;
            Profile previous_autotune_data;
            Profile pumpprofile_data;
            try
            {
                prepped_glucose_data ??= JsonConvert.DeserializeObject<Categorized>(File.ReadAllText(prepped_glucose_input));
                previous_autotune_data = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(previous_autotune_input));
                pumpprofile_data = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(pumpprofile_input));
            }
            catch (Exception e)
            {
                Console.WriteLine("{ \"error\": \"Could not parse input data\" }");
                Console.WriteLine("Could not parse input data: " + e);
                return null;
            }

            // Pump profile has an up to date copy of useCustomPeakTime from preferences
            // If the preferences file has useCustomPeakTime use the previous autotune dia and PeakTime.
            // Otherwise, use data from pump profile.
            if (!pumpprofile_data.useCustomPeakTime)
            {
                previous_autotune_data.dia = pumpprofile_data.dia;
                previous_autotune_data.insulinPeakTime = pumpprofile_data.insulinPeakTime;
            }

            // Always keep the curve value up to date with what's in the user preferences
            previous_autotune_data.curve = pumpprofile_data.curve;

            var opts = new InputClass
            {
                preppedGlucose = prepped_glucose_data,
                previousAutotune = previous_autotune_data,
                pumpProfile = pumpprofile_data
            };

            var autotune_output = AutotuneCore.tuneAllTheThings(opts);
            return autotune_output;
        }

        static string GenerateReport(string pumpprofile_input, string profile_input)
        {
            Profile profile_data;
            try
            {
                profile_data = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(profile_input));
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not parse input data: " + e);
                return null;
            }

            Profile pumpprofile_data;
            try
            {
                pumpprofile_data = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(pumpprofile_input));
            }
            catch (Exception e)
            {
                Console.WriteLine("Warning: could not parse " + pumpprofile_input);
                return null;
            }

            // Get current profile info
            var basal_minutes_current = pumpprofile_data.basalprofile.Select(x => x.start).ToList();
            var basal_rate_current = pumpprofile_data.basalprofile.Select(x => x.rate).ToList();
            var isf_current = pumpprofile_data.isfProfile.sensitivities[0].sensitivity;
            var csf_current = pumpprofile_data.csf;
            var carb_ratio_current = pumpprofile_data.carb_ratio;

            // Get autotune profile info
            var basal_minutes_new = profile_data.basalprofile.Select(x => x.start).ToList();
            var basal_rate_new = profile_data.basalprofile.Select(x => x.rate).ToList();
            var basal_untuned_new = profile_data.basalprofile.Select(x => x.untuned).ToList();
            var isf_new = profile_data.isfProfile.sensitivities[0].sensitivity;
            var csf_new = profile_data.csf;
            var carb_ratio_new = profile_data.carb_ratio;

            var sb = new StringBuilder();

            // Print Header Info
            sb.AppendLine($"{"Parameter",-15}| {"Pump",-12}| {"Autotune",-12}| {"Days Missing",-12}");
            sb.AppendLine("---------------------------------------------------------");

            // Print ISF, CSF and Carb Ratio Recommendations
            sb.AppendLine($"{"ISF [mg/dL/U]",-15}| {isf_current,-12:F3}| {isf_new,-12:F3}|");

            sb.AppendLine($"{"Carb Ratio[g/U]",-15}| {carb_ratio_current,-12:F3}| {carb_ratio_new,-12:F3}|");

            // Print Basal Profile Recommendations
            sb.AppendLine($"{"Basals [U/hr]",-15}| {"-",-12}| {"",-12}|");

            for (int i = 0; i < basal_minutes_new.Count; i++)
            {
                sb.AppendLine($"  {basal_minutes_new[i][..^3],-13}| {basal_rate_current[i],-12:F3}| {basal_rate_new[i],-12:F3}| {basal_untuned_new[i],-12}");
            }

            //sb.AppendLine($"  {"Total",-13}| {basal_rate_current.Sum(),-12:F3}| {basal_rate_new.Sum(),-12:F3}| {basal_untuned_new.Sum(),-12}");

            return sb.ToString();
        }
    }
}
