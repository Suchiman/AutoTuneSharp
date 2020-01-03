using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTune
{
    static class AutotuneCore
    {
        public static Profile tuneAllTheThings(InputClass inputs)
        {
            var previousAutotune = inputs.previousAutotune;
            //Console.WriteLine(previousAutotune);
            var pumpProfile = inputs.pumpProfile;
            var pumpBasalProfile = pumpProfile.basalprofile;
            //Console.WriteLine(pumpBasalProfile);
            var basalProfile = previousAutotune.basalprofile;
            //Console.WriteLine(basalProfile);
            var isfProfile = previousAutotune.isfProfile;
            //Console.WriteLine(isfProfile);
            var ISF = isfProfile.sensitivities[0].sensitivity;
            //Console.WriteLine(ISF);
            var carbRatio = previousAutotune.carb_ratio;
            //Console.WriteLine(carbRatio);
            var CSF = ISF / carbRatio;
            var DIA = previousAutotune.dia;
            var peak = previousAutotune.insulinPeakTime;
            if (!previousAutotune.useCustomPeakTime)
            {
                if (previousAutotune.curve == "ultra-rapid")
                {
                    peak = 55;
                }
                else
                {
                    peak = 75;
                }
            }
            //Console.WriteLine(DIA, peak);

            // conditional on there being a pump profile; if not then skip
            double? pumpISF = null;
            double? pumpCSF = null;
            double? pumpCarbRatio = null;
            if (pumpProfile?.isfProfile?.sensitivities?.FirstOrDefault() is { } sens)
            {
                pumpISF = sens.sensitivity;
                pumpCarbRatio = pumpProfile.carb_ratio;
                pumpCSF = pumpISF / pumpCarbRatio;
                if (!(carbRatio > 0)) { carbRatio = pumpCarbRatio.Value; }
                if (!(CSF > 0)) { CSF = pumpCSF.Value; }
                if (!(ISF > 0)) { ISF = pumpISF.Value; }
            }
            //Console.WriteLine(CSF);
            var preppedGlucose = inputs.preppedGlucose;
            var CSFGlucose = preppedGlucose.CSFGlucoseData;
            //Console.WriteLine(CSFGlucose[0]);
            var ISFGlucose = preppedGlucose.ISFGlucoseData;
            //Console.WriteLine(ISFGlucose[0]);
            var basalGlucose = preppedGlucose.basalGlucoseData;
            //Console.WriteLine(basalGlucose[0]);
            var CRData = preppedGlucose.CRData;
            //Console.WriteLine(CRData);
            var diaDeviations = preppedGlucose.diaDeviations;
            //Console.WriteLine(diaDeviations);
            var peakDeviations = preppedGlucose.peakDeviations;
            //Console.WriteLine(peakDeviations);

            // tune DIA
            var newDIA = DIA;
            if (diaDeviations != null)
            {
                var currentDIAMeanDev = diaDeviations[2].meanDeviation;
                var currentDIARMSDev = diaDeviations[2].RMSDeviation;
                //Console.WriteLine(DIA,currentDIAMeanDev,currentDIARMSDev);
                double minMeanDeviations = 1000000;
                double minRMSDeviations = 1000000;
                int meanBest = 2;
                int RMSBest = 2;
                for (var i = 0; i < diaDeviations.Count; i++)
                {
                    var meanDeviations = diaDeviations[i].meanDeviation;
                    var RMSDeviations = diaDeviations[i].RMSDeviation;
                    if (meanDeviations < minMeanDeviations)
                    {
                        minMeanDeviations = JSMath.Round(meanDeviations, 3);
                        meanBest = i;
                    }
                    if (RMSDeviations < minRMSDeviations)
                    {
                        minRMSDeviations = JSMath.Round(RMSDeviations, 3);
                        RMSBest = i;
                    }
                }
                Console.WriteLine("Best insulinEndTime for meanDeviations: " + diaDeviations[meanBest].dia + " hours");
                Console.WriteLine("Best insulinEndTime for RMSDeviations: " + diaDeviations[RMSBest].dia + " hours");
                if (meanBest < 2 && RMSBest < 2)
                {
                    if (diaDeviations[1].meanDeviation < currentDIAMeanDev * 0.99 && diaDeviations[1].RMSDeviation < currentDIARMSDev * 0.99)
                    {
                        newDIA = diaDeviations[1].dia;
                    }
                }
                else if (meanBest > 2 && RMSBest > 2)
                {
                    if (diaDeviations[3].meanDeviation < currentDIAMeanDev * 0.99 && diaDeviations[3].RMSDeviation < currentDIARMSDev * 0.99)
                    {
                        newDIA = diaDeviations[3].dia;
                    }
                }
                if (newDIA > 12)
                {
                    Console.WriteLine("insulinEndTime maximum is 12h: not raising further");
                    newDIA = 12;
                }
                if (newDIA != DIA)
                {
                    Console.WriteLine("Adjusting insulinEndTime from " + DIA + " to " + newDIA + " hours");
                }
                else
                {
                    Console.WriteLine("Leaving insulinEndTime unchanged at " + DIA + " hours");
                }
            }

            // tune insulinPeakTime
            var newPeak = peak;
            if (peakDeviations?.Count > 2)
            {
                var currentPeakMeanDev = peakDeviations[2].meanDeviation;
                var currentPeakRMSDev = peakDeviations[2].RMSDeviation;
                //Console.WriteLine(currentPeakMeanDev);
                double minMeanDeviations = 1000000;
                double minRMSDeviations = 1000000;
                var meanBest = 2;
                var RMSBest = 2;
                for (int i = 0; i < peakDeviations.Count; i++)
                {
                    var meanDeviations = peakDeviations[i].meanDeviation;
                    var RMSDeviations = peakDeviations[i].RMSDeviation;
                    if (meanDeviations < minMeanDeviations)
                    {
                        minMeanDeviations = JSMath.Round(meanDeviations, 3);
                        meanBest = i;
                    }
                    if (RMSDeviations < minRMSDeviations)
                    {
                        minRMSDeviations = JSMath.Round(RMSDeviations, 3);
                        RMSBest = i;
                    }
                }
                Console.WriteLine("Best insulinPeakTime for meanDeviations: " + peakDeviations[meanBest].peak + " minutes");
                Console.WriteLine("Best insulinPeakTime for RMSDeviations: " + peakDeviations[RMSBest].peak + " minutes");
                if (meanBest < 2 && RMSBest < 2)
                {
                    if (peakDeviations[1].meanDeviation < currentPeakMeanDev * 0.99 && peakDeviations[1].RMSDeviation < currentPeakRMSDev * 0.99)
                    {
                        newPeak = peakDeviations[1].peak;
                    }
                }
                else if (meanBest > 2 && RMSBest > 2)
                {
                    if (peakDeviations[3].meanDeviation < currentPeakMeanDev * 0.99 && peakDeviations[3].RMSDeviation < currentPeakRMSDev * 0.99)
                    {
                        newPeak = peakDeviations[3].peak;
                    }
                }
                if (newPeak != peak)
                {
                    Console.WriteLine("Adjusting insulinPeakTime from " + peak + " to " + newPeak + " minutes");
                }
                else
                {
                    Console.WriteLine("Leaving insulinPeakTime unchanged at " + peak);
                }
            }

            // Calculate carb ratio (CR) independently of CSF and ISF
            // Use the time period from meal bolus/carbs until COB is zero and IOB is < currentBasal/2
            // For now, if another meal IOB/COB stacks on top of it, consider them together
            // Compare beginning and ending BGs, and calculate how much more/less insulin is needed to neutralize
            // Use entered carbs vs. starting IOB + delivered insulin + needed-at-end insulin to directly calculate CR.

            double CRTotalCarbs = 0;
            double CRTotalInsulin = 0;
            foreach (var CRDatum in CRData)
            {
                var CRBGChange = CRDatum.CREndBG - CRDatum.CRInitialBG;
                var CRInsulinReq = CRBGChange / ISF;
                var CRIOBChange = CRDatum.CREndIOB - CRDatum.CRInitialIOB;
                CRDatum.CRInsulinTotal = CRDatum.CRInitialIOB + CRDatum.CRInsulin + CRInsulinReq;
                //Console.WriteLine(CRDatum.CRInitialIOB, CRDatum.CRInsulin, CRInsulinReq, CRDatum.CRInsulinTotal);
                var CR = JSMath.Round(CRDatum.CRCarbs / CRDatum.CRInsulinTotal.Value, 3);
                //Console.WriteLine(CRBGChange, CRInsulinReq, CRIOBChange, CRDatum.CRInsulinTotal);
                //Console.WriteLine("CRCarbs:",CRDatum.CRCarbs,"CRInsulin:",CRDatum.CRInsulin,"CRDatum.CRInsulinTotal:",CRDatum.CRInsulinTotal,"CR:",CR);
                if (CRDatum.CRInsulinTotal > 0)
                {
                    CRTotalCarbs += CRDatum.CRCarbs;
                    CRTotalInsulin += CRDatum.CRInsulinTotal.Value;
                    //Console.WriteLine("CRTotalCarbs:",CRTotalCarbs,"CRTotalInsulin:",CRTotalInsulin);
                }
            }
            CRTotalInsulin = JSMath.Round(CRTotalInsulin, 3);
            var totalCR = JSMath.Round(CRTotalCarbs / CRTotalInsulin, 3);
            Console.WriteLine("CRTotalCarbs: " + CRTotalCarbs + " CRTotalInsulin: " + CRTotalInsulin + " totalCR: " + totalCR);

            // convert the basal profile to hourly if it isn't already
            var hourlyBasalProfile = new Basalprofile[24];
            var hourlyPumpProfile = new Basalprofile[24];
            for (int i = 0; i < 24; i++)
            {
                // autotuned basal profile
                for (var j = 0; j < basalProfile.Count; ++j)
                {
                    if (basalProfile[j].minutes <= i * 60)
                    {
                        if (basalProfile[j].rate == 0)
                        {
                            Console.WriteLine("ERROR: bad basalProfile " + JsonConvert.SerializeObject(basalProfile[j]));
                            return null;
                        }
                        hourlyBasalProfile[i] = JsonConvert.DeserializeObject<Basalprofile>(JsonConvert.SerializeObject(basalProfile[j]));
                    }
                }
                hourlyBasalProfile[i].i = i;
                hourlyBasalProfile[i].minutes = i * 60;
                hourlyBasalProfile[i].start = $"{i:00}:00:00";
                hourlyBasalProfile[i].rate = JSMath.Round(hourlyBasalProfile[i].rate, 3);
                // pump basal profile
                if (pumpBasalProfile?.Count > 0)
                {
                    for (int j = 0; j < pumpBasalProfile.Count; ++j)
                    {
                        //Console.WriteLine(pumpBasalProfile[j]);
                        if (pumpBasalProfile[j].rate == 0)
                        {
                            Console.WriteLine("ERROR: bad pumpBasalProfile " + JsonConvert.SerializeObject(pumpBasalProfile[j]));
                            return null;
                        }
                        if (pumpBasalProfile[j].minutes <= i * 60)
                        {
                            hourlyPumpProfile[i] = JsonConvert.DeserializeObject<Basalprofile>(JsonConvert.SerializeObject(pumpBasalProfile[j]));
                        }
                    }
                    hourlyPumpProfile[i].i = i;
                    hourlyPumpProfile[i].minutes = i * 60;
                    hourlyPumpProfile[i].rate = JSMath.Round(hourlyPumpProfile[i].rate, 3);
                }
            }
            //Console.WriteLine(hourlyPumpProfile);
            //Console.WriteLine(hourlyBasalProfile);
            var newHourlyBasalProfile = JsonConvert.DeserializeObject<List<Basalprofile>>(JsonConvert.SerializeObject(hourlyBasalProfile));

            // look at net deviations for each hour
            for (var hour = 0; hour < 24; hour++)
            {
                double deviations = 0;
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
                        Console.WriteLine("Could not determine last BG time");
                        throw new Exception("Could not determine last BG time");
                    }

                    var myHour = BGTime.Hour;
                    if (hour == myHour)
                    {
                        //Console.WriteLine(basalGlucose[i].deviation);
                        deviations += basalGlucose[i].deviation;
                    }
                }
                deviations = JSMath.Round(deviations, 3);
                Console.WriteLine($"Hour {hour} total deviations: {deviations} mg/dL");
                // calculate how much less or additional basal insulin would have been required to eliminate the deviations
                // only apply 20% of the needed adjustment to keep things relatively stable
                var basalNeeded = 0.2 * deviations / ISF;
                basalNeeded = JSMath.Round(basalNeeded, 2);
                // if basalNeeded is positive, adjust each of the 1-3 hour prior basals by 10% of the needed adjustment
                Console.WriteLine($"Hour {hour} basal adjustment needed: {basalNeeded} U/hr");
                if (basalNeeded > 0)
                {
                    for (var offset = -3; offset < 0; offset++)
                    {
                        var offsetHour = hour + offset;
                        if (offsetHour < 0) { offsetHour += 24; }
                        //Console.WriteLine(offsetHour);
                        newHourlyBasalProfile[offsetHour].rate += basalNeeded / 3;
                        newHourlyBasalProfile[offsetHour].rate = JSMath.Round(newHourlyBasalProfile[offsetHour].rate, 3);
                    }
                    // otherwise, figure out the percentage reduction required to the 1-3 hour prior basals
                    // and adjust all of them downward proportionally
                }
                else if (basalNeeded < 0)
                {
                    double threeHourBasal = 0;
                    for (var offset = -3; offset < 0; offset++)
                    {
                        var offsetHour = hour + offset;
                        if (offsetHour < 0) { offsetHour += 24; }
                        threeHourBasal += newHourlyBasalProfile[offsetHour].rate;
                    }
                    var adjustmentRatio = 1.0 + basalNeeded / threeHourBasal;
                    //Console.WriteLine(adjustmentRatio);
                    for (var offset = -3; offset < 0; offset++)
                    {
                        var offsetHour = hour + offset;
                        if (offsetHour < 0) { offsetHour += 24; }
                        newHourlyBasalProfile[offsetHour].rate = newHourlyBasalProfile[offsetHour].rate * adjustmentRatio;
                        newHourlyBasalProfile[offsetHour].rate = JSMath.Round(newHourlyBasalProfile[offsetHour].rate, 3);
                    }
                }
            }

            // cap adjustments at autosens_max and autosens_min
            double autotuneMax;
            double autotuneMin;
            if (pumpProfile.autosens_max > 0)
            {
                autotuneMax = pumpProfile.autosens_max;
            }
            else
            {
                autotuneMax = 1.2;
            }
            if (pumpProfile.autosens_min > 0)
            {
                autotuneMin = pumpProfile.autosens_min;
            }
            else
            {
                autotuneMin = 0.7;
            }

            if (pumpBasalProfile?.Count > 0)
            {
                for (int hour = 0; hour < 24; hour++)
                {
                    //Console.WriteLine(newHourlyBasalProfile[hour],hourlyPumpProfile[hour].rate*1.2);
                    var maxRate = hourlyPumpProfile[hour].rate * autotuneMax;
                    var minRate = hourlyPumpProfile[hour].rate * autotuneMin;
                    if (newHourlyBasalProfile[hour].rate > maxRate)
                    {
                        Console.WriteLine($"Limiting hour {hour} basal to {maxRate:F2} (which is {autotuneMax} * pump basal of {hourlyPumpProfile[hour].rate} )");
                        //Console.WriteLine("Limiting hour",hour,"basal to",maxRate.toFixed(2),"(which is 20% above pump basal of",hourlyPumpProfile[hour].rate,")");
                        newHourlyBasalProfile[hour].rate = maxRate;
                    }
                    else if (newHourlyBasalProfile[hour].rate < minRate)
                    {
                        Console.WriteLine($"Limiting hour {hour} basal to {minRate:F2} (which is {autotuneMin} * pump basal of {hourlyPumpProfile[hour].rate} )");
                        //Console.WriteLine("Limiting hour",hour,"basal to",minRate.toFixed(2),"(which is 20% below pump basal of",hourlyPumpProfile[hour].rate,")");
                        newHourlyBasalProfile[hour].rate = minRate;
                    }
                    newHourlyBasalProfile[hour].rate = JSMath.Round(newHourlyBasalProfile[hour].rate, 3);
                }
            }

            // some hours of the day rarely have data to tune basals due to meals.
            // when no adjustments are needed to a particular hour, we should adjust it toward the average of the
            // periods before and after it that do have data to be tuned

            var lastAdjustedHour = 0;
            // scan through newHourlyBasalProfile and find hours where the rate is unchanged
            for (int hour = 0; hour < 24; hour++)
            {
                if (hourlyBasalProfile[hour].rate == newHourlyBasalProfile[hour].rate)
                {
                    var nextAdjustedHour = 23;
                    for (var nextHour = hour; nextHour < 24; nextHour++)
                    {
                        if (!(hourlyBasalProfile[nextHour].rate == newHourlyBasalProfile[nextHour].rate))
                        {
                            nextAdjustedHour = nextHour;
                            break;
                            //} else {
                            //Console.WriteLine(nextHour, hourlyBasalProfile[nextHour].rate, newHourlyBasalProfile[nextHour].rate);
                        }
                    }
                    //Console.WriteLine(hour, newHourlyBasalProfile);
                    newHourlyBasalProfile[hour].rate = JSMath.Round(0.8 * hourlyBasalProfile[hour].rate + 0.1 * newHourlyBasalProfile[lastAdjustedHour].rate + 0.1 * newHourlyBasalProfile[nextAdjustedHour].rate, 3);
                    newHourlyBasalProfile[hour].untuned++;
                    Console.WriteLine($"Adjusting hour {hour} basal from {hourlyBasalProfile[hour].rate} to {newHourlyBasalProfile[hour].rate} based on hour {lastAdjustedHour} = {newHourlyBasalProfile[lastAdjustedHour].rate} and hour {nextAdjustedHour} = {newHourlyBasalProfile[nextAdjustedHour].rate}");
                }
                else
                {
                    lastAdjustedHour = hour;
                }
            }

            Console.WriteLine(JsonConvert.SerializeObject(newHourlyBasalProfile, Formatting.Indented));
            basalProfile = newHourlyBasalProfile;

            // Calculate carb ratio (CR) independently of CSF and ISF
            // Use the time period from meal bolus/carbs until COB is zero and IOB is < currentBasal/2
            // For now, if another meal IOB/COB stacks on top of it, consider them together
            // Compare beginning and ending BGs, and calculate how much more/less insulin is needed to neutralize
            // Use entered carbs vs. starting IOB + delivered insulin + needed-at-end insulin to directly calculate CR.



            // calculate net deviations while carbs are absorbing
            // measured from carb entry until COB and deviations both drop to zero

            var mealCarbs = 0;
            var totalMealCarbs = 0;
            double totalDeviations = 0;
            double fullNewCSF;
            //Console.WriteLine(CSFGlucose[0].mealAbsorption);
            //Console.WriteLine(CSFGlucose[0]);
            {
                double deviations = 0;
                for (int i = 0; i < CSFGlucose.Count; ++i)
                {
                    //Console.WriteLine(CSFGlucose[i].mealAbsorption, i);
                    if (CSFGlucose[i].mealAbsorption == "start")
                    {
                        deviations = 0;
                        mealCarbs = CSFGlucose[i].mealCarbs;
                    }
                    else if (CSFGlucose[i].mealAbsorption == "end")
                    {
                        deviations += CSFGlucose[i].deviation;
                        // compare the sum of deviations from start to end vs. current CSF * mealCarbs
                        //Console.WriteLine(CSF,mealCarbs);
                        var csfRise = CSF * mealCarbs;
                        //Console.WriteLine(deviations,ISF);
                        //Console.WriteLine("csfRise:",csfRise,"deviations:",deviations);
                        totalMealCarbs += mealCarbs;
                        totalDeviations += deviations;
                    }
                    else
                    {
                        deviations += Math.Max(0 * previousAutotune.min_5m_carbimpact, CSFGlucose[i].deviation);
                        mealCarbs = Math.Max(mealCarbs, CSFGlucose[i].mealCarbs);
                    }
                }
                // at midnight, write down the mealcarbs as total meal carbs (to prevent special case of when only one meal and it not finishing absorbing by midnight)
                // TODO: figure out what to do with dinner carbs that don't finish absorbing by midnight
                if (totalMealCarbs == 0) { totalMealCarbs += mealCarbs; }
                if (totalDeviations == 0) { totalDeviations += deviations; }
            }
            //Console.WriteLine(totalDeviations, totalMealCarbs);
            if (totalMealCarbs == 0)
            {
                // if no meals today, CSF is unchanged
                fullNewCSF = CSF;
            }
            else
            {
                // how much change would be required to account for all of the deviations
                fullNewCSF = JSMath.Round((totalDeviations / totalMealCarbs), 2);
            }
            // only adjust by 20%
            var newCSF = (0.8 * CSF) + (0.2 * fullNewCSF);
            // safety cap CSF
            if (pumpCSF.HasValue)
            {
                var maxCSF = pumpCSF.Value * autotuneMax;
                var minCSF = pumpCSF.Value * autotuneMin;
                if (newCSF > maxCSF)
                {
                    Console.WriteLine($"Limiting CSF to {maxCSF:F2} (which is {autotuneMax} * pump CSF of {pumpCSF} )");
                    newCSF = maxCSF;
                }
                else if (newCSF < minCSF)
                {
                    Console.WriteLine($"Limiting CSF to {minCSF:F2} (which is {autotuneMin} * pump CSF of {pumpCSF} )");
                    newCSF = minCSF;
                } //else { Console.WriteLine("newCSF",newCSF,"is close enough to",pumpCSF); }
            }
            var oldCSF = JSMath.Round(CSF, 3);
            newCSF = JSMath.Round(newCSF, 3);
            totalDeviations = JSMath.Round(totalDeviations, 3);
            Console.WriteLine($"totalMealCarbs: {totalMealCarbs} totalDeviations: {totalDeviations} oldCSF {oldCSF} fullNewCSF: {fullNewCSF} newCSF: {newCSF}");
            // this is where CSF is set based on the outputs
            if (newCSF > 0)
            {
                CSF = newCSF;
            }

            double fullNewCR;
            if (totalCR == 0)
            {
                // if no meals today, CR is unchanged
                fullNewCR = carbRatio;
            }
            else
            {
                // how much change would be required to account for all of the deviations
                fullNewCR = totalCR;
            }
            // don't tune CR out of bounds
            var maxCR = pumpCarbRatio * autotuneMax;
            if (maxCR > 150) { maxCR = 150; }
            var minCR = pumpCarbRatio * autotuneMin;
            if (minCR < 3) { minCR = 3; }
            // safety cap fullNewCR
            if (pumpCarbRatio.HasValue)
            {
                if (fullNewCR > maxCR)
                {
                    Console.WriteLine($"Limiting fullNewCR from {fullNewCR} to {maxCR:F2} (which is {autotuneMax} * pump CR of {pumpCarbRatio} )");
                    fullNewCR = maxCR.Value;
                }
                else if (fullNewCR < minCR)
                {
                    Console.WriteLine($"Limiting fullNewCR from {fullNewCR} to {minCR:F2} (which is {autotuneMin} * pump CR of {pumpCarbRatio} )");
                    fullNewCR = minCR.Value;
                } //else { Console.WriteLine("newCR",newCR,"is close enough to",pumpCarbRatio); }
            }
            // only adjust by 20%
            var newCR = (0.8 * carbRatio) + (0.2 * fullNewCR);
            // safety cap newCR
            if (pumpCarbRatio.HasValue)
            {
                if (newCR > maxCR)
                {
                    Console.WriteLine($"Limiting CR to {maxCR:F2} (which is {autotuneMax} * pump CR of {pumpCarbRatio} )");
                    newCR = maxCR.Value;
                }
                else if (newCR < minCR)
                {
                    Console.WriteLine($"Limiting CR to {minCR:F2} (which is {autotuneMin} * pump CR of {pumpCarbRatio} )");
                    newCR = minCR.Value;
                } //else { Console.WriteLine("newCR",newCR,"is close enough to",pumpCarbRatio); }
            }
            newCR = JSMath.Round(newCR, 3);
            Console.WriteLine($"oldCR: {carbRatio} fullNewCR: {fullNewCR} newCR: {newCR}");
            // this is where CR is set based on the outputs
            //var ISFFromCRAndCSF = ISF;
            if (newCR > 0)
            {
                carbRatio = newCR;
                //ISFFromCRAndCSF = JSMath.Round( carbRatio * CSF * 1000)/1000;
            }

            // calculate median deviation and bgi in data attributable to ISF
            var deviationList = new List<double>();
            var BGIs = new List<double>();
            var avgDeltas = new List<double>();
            var ratios = new List<double>();
            for (int i = 0; i < ISFGlucose.Count; ++i)
            {
                var deviation = ISFGlucose[i].deviation;
                deviationList.Add(deviation);
                var BGI = ISFGlucose[i].BGI;
                BGIs.Add(BGI);
                var avgDelta = ISFGlucose[i].avgDelta;
                avgDeltas.Add(avgDelta);
                var ratio = 1 + deviation / BGI;
                //Console.WriteLine("Deviation:",deviation,"BGI:",BGI,"avgDelta:",avgDelta,"ratio:",ratio);
                ratios.Add(ratio);
            }
            avgDeltas.Sort((a, b) => a.CompareTo(b));
            BGIs.Sort((a, b) => a.CompareTo(b));
            deviationList.Sort((a, b) => a.CompareTo(b));
            ratios.Sort((a, b) => a.CompareTo(b));
            var p50deviation = percentile(deviationList, 0.50);
            var p50BGI = percentile(BGIs, 0.50);
            var p50ratios = JSMath.Round(percentile(ratios, 0.50), 3);
            var fullNewISF = ISF;
            if (ISFGlucose.Count < 10)
            {
                // leave ISF unchanged if fewer than 5 ISF data points
                Console.WriteLine($"Only found {ISFGlucose.Count} ISF data points, leaving ISF unchanged at {ISF}");
            }
            else
            {
                // calculate what adjustments to ISF would have been necessary to bring median deviation to zero
                fullNewISF = ISF * p50ratios;
            }
            fullNewISF = JSMath.Round(fullNewISF, 3);
            //Console.WriteLine("p50ratios:",p50ratios,"fullNewISF:",fullNewISF,ratios[Math.floor(ratios.length/2)]);

            // adjust the target ISF to be a weighted average of fullNewISF and pumpISF
            double adjustmentFraction;
            if (pumpProfile.autotune_isf_adjustmentFraction > 0)
            {
                adjustmentFraction = pumpProfile.autotune_isf_adjustmentFraction;
            }
            else
            {
                adjustmentFraction = 1.0;
            }

            // low autosens ratio = high ISF
            var maxISF = pumpISF / autotuneMin;
            // high autosens ratio = low ISF
            var minISF = pumpISF / autotuneMax;
            double? adjustedISF = null;
            double? newISF = null;
            if (pumpISF.HasValue)
            {
                if (fullNewISF < 0)
                {
                    adjustedISF = ISF;
                }
                else
                {
                    adjustedISF = adjustmentFraction * fullNewISF + (1 - adjustmentFraction) * pumpISF.Value;
                }
                // cap adjustedISF before applying 10%
                //Console.WriteLine(adjustedISF, maxISF, minISF);
                if (adjustedISF > maxISF)
                {
                    Console.WriteLine($"Limiting adjusted ISF of {adjustedISF:F2} to {maxISF:F2} (which is pump ISF of {pumpISF} / {autotuneMin} )");
                    adjustedISF = maxISF.Value;
                }
                else if (adjustedISF < minISF)
                {
                    Console.WriteLine($"Limiting adjusted ISF of {adjustedISF:F2} to {minISF:F2} (which is pump ISF of {pumpISF} / {autotuneMax} )");
                    adjustedISF = minISF.Value;
                }

                // and apply 20% of that adjustment
                newISF = (0.8 * ISF) + (0.2 * adjustedISF);

                if (newISF > maxISF)
                {
                    Console.WriteLine($"Limiting ISF of {newISF:F2} to {maxISF:F2} (which is pump ISF of {pumpISF} / {autotuneMin} )");
                    newISF = maxISF.Value;
                }
                else if (newISF < minISF)
                {
                    Console.WriteLine($"Limiting ISF of {newISF:F2} to {minISF:F2} (which is pump ISF of {pumpISF} / {autotuneMax} )");
                    newISF = minISF.Value;
                }

                newISF = JSMath.Round(newISF.Value, 3);
                adjustedISF = JSMath.Round(adjustedISF.Value, 3);
            }
            //Console.WriteLine(avgRatio);
            //Console.WriteLine(newISF);
            p50deviation = JSMath.Round(p50deviation, 3);
            p50BGI = JSMath.Round(p50BGI, 3);
            Console.WriteLine($"p50deviation: {p50deviation} p50BGI {p50BGI} p50ratios: {p50ratios} Old ISF: {ISF} fullNewISF: {fullNewISF} adjustedISF: {adjustedISF} newISF: {newISF} newDIA: {newDIA} newPeak: {newPeak}");

            if (newISF.HasValue)
            {
                ISF = newISF.Value;
            }


            // reconstruct updated version of previousAutotune as autotuneOutput
            var autotuneOutput = previousAutotune;
            autotuneOutput.basalprofile = basalProfile;
            isfProfile.sensitivities[0].sensitivity = ISF;
            autotuneOutput.isfProfile = isfProfile;
            autotuneOutput.sens = ISF;
            autotuneOutput.csf = CSF;
            //carbRatio = ISF / CSF;
            carbRatio = JSMath.Round(carbRatio, 3);
            autotuneOutput.carb_ratio = carbRatio;
            autotuneOutput.dia = newDIA;
            autotuneOutput.insulinPeakTime = newPeak;
            if (diaDeviations != null || peakDeviations != null)
            {
                autotuneOutput.useCustomPeakTime = true;
            }

            return autotuneOutput;
        }

        static double percentile(List<double> arr, double p)
        {
            if (arr.Count == 0) return 0;
            if (p <= 0) return arr[0];
            if (p >= 1) return arr[arr.Count - 1];

            var index = arr.Count * p;
            var lower = (int)index;
            var upper = lower + 1;
            var weight = index % 1;

            if (upper >= arr.Count) return arr[lower];
            return arr[lower] * (1 - weight) + arr[upper] * weight;
        }
    }
}
