using System;
using System.Linq;

namespace AutoTune
{
    class ISF
    {
        Sensitivity lastResult = null;

        public double isfLookup(Isfprofile isf_data, DateTimeOffset timestamp)
        {
            var nowDate = timestamp;

            var nowMinutes = nowDate.Hour * 60 + nowDate.Minute;

            if (lastResult != null && nowMinutes >= lastResult.offset && nowMinutes < lastResult.endoffset) // WTF: this used to endOffset but in the json endoffset already exists
            {
                return lastResult.sensitivity;
            }

            var sorted = isf_data.sensitivities.OrderBy(o => o.offset).ToList();

            var isfSchedule = sorted[sorted.Count - 1];

            if (sorted[0].offset != 0)
            {
                return -1;
            }

            // TODO: why does this recompute endoffset?
            var endMinutes = 1440;
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                var currentISF = sorted[i];
                var nextISF = sorted[i + 1];
                if (nowMinutes >= currentISF.offset && nowMinutes < nextISF.offset)
                {
                    endMinutes = nextISF.offset;
                    isfSchedule = sorted[i];
                    break;
                }
            }

            lastResult = isfSchedule;
            lastResult.endoffset = endMinutes;

            return isfSchedule.sensitivity;
        }
    }
}
