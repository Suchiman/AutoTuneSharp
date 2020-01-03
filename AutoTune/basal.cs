using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTune
{
    static class basal
    {
        /// <summary>
        /// Return basal rate(U / hr) at the provided timeOfDay
        /// </summary>
        public static double basalLookup(List<Basalprofile> schedules, DateTimeOffset? now)
        {
            var nowDate = now?.ToLocalTime();

            var basalprofile_data = schedules.OrderBy(o => o.minutes).ToList(); // o => o.i? which i?
            var basalRate = basalprofile_data[basalprofile_data.Count - 1].rate;
            if (basalRate == 0)
            {
                Console.WriteLine("ERROR: bad basal schedule " + JsonConvert.SerializeObject(schedules));
                throw new Exception("ERROR: bad basal schedule");
            }

            double nowMinutes = (nowDate?.Hour ?? double.NaN) * 60 + (nowDate?.Minute ?? double.NaN);

            for (var i = 0; i < basalprofile_data.Count - 1; i++)
            {
                if ((nowMinutes >= basalprofile_data[i].minutes) && (nowMinutes < basalprofile_data[i + 1].minutes))
                {
                    basalRate = basalprofile_data[i].rate;
                    break;
                }
            }
            return JSMath.Round(basalRate, 3);
        }

        //public static void maxDailyBasal (T inputs) {
        //    var maxRate = _.maxBy(inputs.basals,o=> Number(o.rate));
        //    return (Number(maxRate.rate) *1000)/1000;
        //}

        ///*Return maximum daily basal rate(U / hr) from profile.basals */
        //public static void maxBasalLookup (T inputs) {
        //    return inputs.settings.maxBasal;
        //}
    }
}
