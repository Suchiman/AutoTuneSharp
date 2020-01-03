using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTune
{
    static class History
    {
        static List<TempHistory> splitTimespanWithOneSplitter(TempHistory @event, SplitterEvent splitter)
        {
            if (splitter.type == "recurring")
            {
                var startMinutes = @event.started_at.Hour * 60 + @event.started_at.Minute;
                var endMinutes = startMinutes + @event.duration;

                // 1440 = one day; no clean way to check if the event overlaps midnight
                // so checking if end of event in minutes is past midnight
                if (@event.duration > 30 || (startMinutes < splitter.minutes && endMinutes > splitter.minutes) || (endMinutes > 1440 && splitter.minutes < (endMinutes - 1440)))
                {
                    var event1 = @event.Clone();
                    var event2 = @event.Clone();

                    int event1Duration;
                    if (@event.duration > 30)
                    {
                        event1Duration = 30;
                    }
                    else
                    {
                        var splitPoint = splitter.minutes;
                        if (endMinutes > 1440) { splitPoint = 1440; }
                        event1Duration = splitPoint - startMinutes;
                    }

                    var event1EndDate = @event.started_at.AddMinutes(event1Duration);

                    event1.duration = event1Duration;

                    event2.duration = @event.duration - event1Duration;
                    event2.timestamp = event1EndDate.ToUnixTimeMilliseconds();
                    event2.started_at = event1EndDate;
                    event2.date = event2.started_at.ToUnixTimeMilliseconds();

                    return new List<TempHistory> { event1, event2 };
                }
            }

            return new List<TempHistory> { @event };
        }

        static List<TempHistory> splitTimespan(TempHistory @event, List<SplitterEvent> splitterMoments)
        {
            var results = new List<TempHistory> { @event };

            var splitFound = true;
            while (splitFound)
            {
                var resultArray = new List<TempHistory>();
                splitFound = false;

                foreach (var o in results)
                {
                    foreach (var p in splitterMoments)
                    {
                        var splitResult = splitTimespanWithOneSplitter(o, p);
                        if (splitResult.Count > 1)
                        {
                            resultArray.AddRange(splitResult);
                            splitFound = true;
                            break;
                        }
                    }

                    if (!splitFound)
                    {
                        resultArray.Add(o);
                    }
                }

                results = resultArray;
            }

            return results;
        }

        // Split currentEvent around any conflicting suspends
        // by removing the time period from the event that
        // overlaps with any suspend.
        static List<TempHistory> splitAroundSuspends(TempHistory currentEvent, List<InsulinRecord> pumpSuspends, long firstResumeTime, bool suspendedPrior, long lastSuspendTime, bool currentlySuspended)
        {
            var events = new List<TempHistory>();

            var firstResumeStarted = DateTimeOffset.FromUnixTimeMilliseconds(firstResumeTime).ToLocalTime();
            var firstResumeDate = firstResumeStarted.ToUnixTimeMilliseconds();

            var lastSuspendStarted = DateTimeOffset.FromUnixTimeMilliseconds(lastSuspendTime).ToLocalTime();
            var lastSuspendDate = lastSuspendStarted.ToUnixTimeMilliseconds();

            if (suspendedPrior && (currentEvent.date < firstResumeDate))
            {
                if ((currentEvent.date + currentEvent.duration * 60 * 1000) < firstResumeDate)
                {
                    currentEvent.duration = 0;
                }
                else
                {
                    currentEvent.duration = ((currentEvent.date + currentEvent.duration * 60 * 1000) - firstResumeDate) / 60 / 1000;

                    currentEvent.started_at = firstResumeStarted;
                    currentEvent.date = firstResumeDate;
                }
            }

            if (currentlySuspended && ((currentEvent.date + currentEvent.duration * 60 * 1000) > lastSuspendTime))
            {
                if (currentEvent.date > lastSuspendTime)
                {
                    currentEvent.duration = 0;
                }
                else
                {
                    currentEvent.duration = (firstResumeDate - currentEvent.date) / 60 / 1000;
                }
            }

            events.Add(currentEvent);

            if (currentEvent.duration == 0)
            {
                // bail out rather than wasting time going through the rest of the suspend events
                return events;
            }

            for (var i = 0; i < pumpSuspends.Count; i++)
            {
                var suspend = pumpSuspends[i];

                for (var j = 0; j < events.Count; j++)
                {

                    if ((events[j].date <= suspend.date) && (events[j].date + events[j].duration * 60 * 1000) > suspend.date)
                    {
                        // event started before the suspend, but finished after the suspend started

                        if ((events[j].date + events[j].duration * 60 * 1000) > (suspend.date + suspend.duration * 60 * 1000))
                        {
                            var event2 = events[j].Clone();

                            // WTF these calculations
                            var event2StartDate = suspend.started_at.AddMinutes(suspend.duration);

                            event2.timestamp = event2StartDate.ToUnixTimeMilliseconds();
                            event2.started_at = event2StartDate;
                            event2.date = suspend.date + suspend.duration * 60 * 1000;

                            event2.duration = ((events[j].date + events[j].duration * 60 * 1000) - (suspend.date + suspend.duration * 60 * 1000)) / 60 / 1000;

                            events.Add(event2);
                        }

                        events[j].duration = (suspend.date - events[j].date) / 60 / 1000;

                    }
                    else if ((suspend.date <= events[j].date) && (suspend.date + suspend.duration * 60 * 1000 > events[j].date))
                    {
                        // suspend started before the event, but finished after the event started

                        events[j].duration = ((events[j].date + events[j].duration * 60 * 1000) - (suspend.date + suspend.duration * 60 * 1000)) / 60 / 1000;

                        var eventStartDate = suspend.started_at.AddMinutes(suspend.duration);

                        events[j].timestamp = eventStartDate.ToUnixTimeMilliseconds();
                        events[j].started_at = eventStartDate;
                        events[j].date = suspend.date + suspend.duration * 60 * 1000;
                    }
                }
            }

            return events;
        }

        /// <summary>
        /// calcTempTreatments
        /// </summary>
        public static List<TempData> find_insulin(FindInsulin inputs, int zeroTempDuration = 0)
        {
            var pumpHistory = inputs.history;
            var pumpHistory24 = inputs.history24;
            var profile_data = inputs.profile;
            var autosens_data = inputs.autosens;
            var tempHistory = new List<TempHistory>();
            var tempBoluses = new List<TempBolus>();
            var pumpSuspends = new List<InsulinRecord>();
            var pumpResumes = new List<InsulinRecord>();
            var suspendedPrior = false;
            long firstResumeTime = 0;
            long lastSuspendTime = 0;
            var currentlySuspended = false;
            var suspendError = false;

            DateTimeOffset now; // WTF: implicit creation of "now" through inputs.clock === undefined
            if (inputs.clock > DateTimeOffset.MinValue)
            {
                now = inputs.clock.ToLocalTime();
            }
            else
            {
                now = DateTimeOffset.Now;
            }

            if (inputs.history24 != null)
            {
                pumpHistory = inputs.history.Concat(inputs.history24).ToList();
            }

            var lastRecordTime = now;

            // Gather the times the pump was suspended and resumed
            for (var i = 0; i < pumpHistory.Count; i++)
            {
                var temp = new InsulinRecord();

                var current = pumpHistory[i];

                if (current._type == "PumpSuspend")
                {
                    temp.timestamp = current.timestamp;
                    temp.started_at = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value).ToLocalTime();
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    pumpSuspends.Add(temp);
                }
                else if (current._type == "PumpResume")
                {
                    temp.timestamp = current.timestamp;
                    temp.started_at = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value).ToLocalTime();
                    temp.date = temp.started_at.ToUnixTimeSeconds();
                    pumpResumes.Add(temp);
                }
            }

            pumpSuspends = pumpSuspends.OrderBy(x => x.date).ToList();

            pumpResumes = pumpResumes.OrderBy(x => x.date).ToList();

            if (pumpResumes.Count > 0)
            {
                firstResumeTime = pumpResumes[0].timestamp.Value;

                // Check to see if our first resume was prior to our first suspend
                // indicating suspend was prior to our first event.
                if (pumpSuspends.Count == 0 || (pumpResumes[0].date < pumpSuspends[0].date))
                {
                    suspendedPrior = true;
                }

            }

            {
                int ps = 0;
                int j = 0;  // matching pumpResumes entry;

                // Match the resumes with the suspends to get durations
                for (; ps < pumpSuspends.Count; ps++)
                {
                    for (; j < pumpResumes.Count; j++)
                    {
                        if (pumpResumes[j].date > pumpSuspends[ps].date)
                        {
                            break;
                        }
                    }

                    if ((j >= pumpResumes.Count) && !currentlySuspended)
                    {
                        // even though it isn't the last suspend, we have reached
                        // the final suspend. Set resume last so the
                        // algorithm knows to suspend all the way
                        // through the last record beginning at the last suspend
                        // since we don't have a matching resume.
                        currentlySuspended = true; // this was 1?!
                        lastSuspendTime = pumpSuspends[ps].timestamp.Value;

                        break;
                    }

                    pumpSuspends[ps].duration = (pumpResumes[j].date - pumpSuspends[ps].date) / 60 / 1000;

                }

                // These checks indicate something isn't quite aligned.
                // Perhaps more resumes that suspends or vice versa...
                if (!suspendedPrior && !currentlySuspended && (pumpResumes.Count != pumpSuspends.Count))
                {
                    Console.WriteLine("Mismatched number of resumes(" + pumpResumes.Count + ") and suspends(" + pumpSuspends.Count + ")!");
                }
                else if (suspendedPrior && !currentlySuspended && ((pumpResumes.Count - 1) != pumpSuspends.Count))
                {
                    Console.WriteLine("Mismatched number of resumes(" + pumpResumes.Count + ") and suspends(" + pumpSuspends.Count + ") assuming suspended prior to history block!");
                }
                else if (!suspendedPrior && currentlySuspended && (pumpResumes.Count != (pumpSuspends.Count - 1)))
                {
                    Console.WriteLine("Mismatched number of resumes(" + pumpResumes.Count + ") and suspends(" + pumpSuspends.Count + ") assuming suspended past end of history block!");
                }
                else if (suspendedPrior && currentlySuspended && (pumpResumes.Count != pumpSuspends.Count))
                {
                    Console.WriteLine("Mismatched number of resumes(" + pumpResumes.Count + ") and suspends(" + pumpSuspends.Count + ") assuming suspended prior to and past end of history block!");
                }

                if (ps < (pumpSuspends.Count - 1))
                {
                    // truncate any extra suspends. if we had any extras
                    // the error checks above would have issued a error log message
                    pumpSuspends.RemoveRange(ps + 1, pumpSuspends.Count - ps - 1);
                }
            }

            // Pick relevant events for processing and clean the data

            for (int i = 0; i < pumpHistory.Count; i++)
            {
                var current = pumpHistory[i];
                // WTF?
                //if (current.bolus && current.bolus._type == "Bolus") {
                //    var temp = current;
                //    current = temp.bolus;
                //}
                if (current.created_at > DateTimeOffset.MinValue)
                {
                    current.timestamp = current.created_at.ToUnixTimeMilliseconds();
                }
                var currentRecordTime = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value).ToLocalTime();
                //console.error(current);
                //console.error(currentRecordTime,lastRecordTime);
                // ignore duplicate or out-of-order records (due to 1h and 24h overlap, or timezone changes)
                if (currentRecordTime > lastRecordTime)
                {
                    //console.error("",currentRecordTime," > ",lastRecordTime);
                    //process.stderr.write(".");
                    continue;
                }
                else
                {
                    lastRecordTime = currentRecordTime;
                }
                if (current._type == "Bolus")
                {
                    var temp = new TempBolus();
                    temp.timestamp = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value).ToLocalTime();
                    temp.started_at = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value).ToLocalTime();
                    if (temp.started_at > now)
                    {
                        //console.error("Warning: ignoring",current.amount,"U bolus in the future at",temp.started_at);
                        Console.Error.WriteLine(" " + current.amount + "U @ " + temp.started_at);
                    }
                    else
                    {
                        temp.date = temp.started_at.ToUnixTimeMilliseconds();
                        temp.insulin = current.amount;
                        tempBoluses.Add(temp);
                    }
                }
                else if (current.eventType == "Meal Bolus" || current.eventType == "Correction Bolus" || current.eventType == "Snack Bolus" || current.eventType == "Bolus Wizard")
                {
                    //imports treatments entered through Nightscout Care Portal
                    //"Bolus Wizard" refers to the Nightscout Bolus Wizard, not the Medtronic Bolus Wizard
                    var temp = new TempBolus();
                    temp.timestamp = current.created_at;
                    temp.started_at = temp.timestamp;
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    temp.insulin = current.insulin; // WTF: this can be null?
                    tempBoluses.Add(temp);
                }
                else if (current.enteredBy == "xdrip")
                {
                    var temp = new TempBolus();
                    temp.timestamp = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value).ToLocalTime();
                    temp.started_at = temp.timestamp;
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    temp.insulin = current.insulin;
                    tempBoluses.Add(temp);
                }
                else if (current.enteredBy == "HAPP_App" && current.insulin > 0)
                {
                    var temp = new TempBolus();
                    temp.timestamp = current.created_at;
                    temp.started_at = temp.timestamp;
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    temp.insulin = current.insulin;
                    tempBoluses.Add(temp);
                }
                else if (current.eventType == "Temp Basal" && (current.enteredBy == "HAPP_App" || current.enteredBy == "openaps://AndroidAPS"))
                {
                    var temp = new TempHistory();
                    temp.rate = current.absolute;
                    temp.duration = current.duration;
                    temp.timestamp = current.created_at.ToUnixTimeMilliseconds();
                    temp.started_at = DateTimeOffset.FromUnixTimeMilliseconds(temp.timestamp.Value).ToLocalTime();
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    tempHistory.Add(temp);
                }
                else if (current.eventType == "Temp Basal")
                {
                    var temp = new TempHistory();
                    temp.rate = current.rate;
                    temp.duration = current.duration;
                    temp.timestamp = current.timestamp;
                    temp.started_at = DateTimeOffset.FromUnixTimeMilliseconds(temp.timestamp.Value).ToLocalTime();
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    tempHistory.Add(temp);
                }
                else if (current._type == "TempBasal")
                {
                    throw new NotImplementedException("_type = TempBasal");
                    //if (current.temp == "percent")
                    //{
                    //    continue;
                    //}
                    //var rate = current.rate;
                    //var timestamp = current.timestamp;
                    //int? duration = null;
                    //if (i > 0 && pumpHistory[i - 1].timestamp == timestamp && pumpHistory[i - 1]._type == "TempBasalDuration")
                    //{
                    //    duration = pumpHistory[i - 1]["duration (min)"];
                    //}
                    //else
                    //{
                    //    for (var iter = 0; iter < pumpHistory.Count; iter++)
                    //    {
                    //        if (pumpHistory[iter].timestamp == timestamp && pumpHistory[iter]._type == "TempBasalDuration")
                    //        {
                    //            duration = pumpHistory[iter]["duration (min)"];
                    //            break;
                    //        }
                    //    }

                    //    if (duration == null)
                    //    {
                    //        Console.WriteLine("No duration found for " + rate + " U/hr basal " + timestamp, pumpHistory[i - 1], current, pumpHistory[i + 1]); // WTF: aaand? lets just move on?
                    //    }
                    //}
                    //var temp = new TempHistory();
                    //temp.rate = rate;
                    //temp.timestamp = current.timestamp;
                    //temp.started_at = DateTimeOffset.FromUnixTimeMilliseconds(temp.timestamp.Value);
                    //temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    //temp.duration = duration;
                    //tempHistory.Add(temp);
                }
                {
                    // Add a temp basal cancel event to ignore future temps and reduce predBG oscillation
                    var temp = new TempHistory();
                    temp.rate = 0;
                    // start the zero temp 1m in the future to avoid clock skew
                    temp.started_at = now.AddMinutes(1);
                    temp.date = temp.started_at.ToUnixTimeMilliseconds();
                    //temp.timestamp = temp.date; // WTF: the original code doesn't set this so basalLookup would work with an "Invalid Date"
                    temp.duration = zeroTempDuration;
                    tempHistory.Add(temp);
                }
            }

            // Check for overlapping events and adjust event lengths in case of overlap

            tempHistory = tempHistory.OrderBy(x => x.date).ToList();

            for (int i = 0; i + 1 < tempHistory.Count; i++)
            {
                if (tempHistory[i].date + tempHistory[i].duration * 60 * 1000 > tempHistory[i + 1].date)
                {
                    tempHistory[i].duration = (tempHistory[i + 1].date - tempHistory[i].date) / 60d / 1000;
                    // Delete AndroidAPS "Cancel TBR records" in which duration is not populated
                    if (tempHistory[i + 1].duration == 0) // was === null
                    {
                        //tempHistory.RemoveAt(i + 1); WTF
                    }
                }
            }

            // Create an array of moments to slit the temps by
            // currently supports basal changes

            var splitterEvents = new List<SplitterEvent>();

            foreach (var o in profile_data.basalprofile)
            {
                var splitterEvent = new SplitterEvent();
                splitterEvent.type = "recurring";
                splitterEvent.minutes = o.minutes;
                splitterEvents.Add(splitterEvent);
            }

            // iterate through the events and split at basal break points if needed

            var splitHistoryByBasal = new List<TempHistory>();
            foreach (var o in tempHistory)
            {
                splitHistoryByBasal.AddRange(splitTimespan(o, splitterEvents));
            }

            // WTF: pointless?
            tempHistory = tempHistory.OrderBy(x => x.date).ToList();

            var splitHistory = new List<TempHistory>();
            var suspend_zeros_iob = profile_data.suspend_zeros_iob;
            if (suspend_zeros_iob)
            {
                // iterate through the events and adjust their 
                // times as required to account for pump suspends

                foreach (var o in splitHistoryByBasal)
                {
                    var splitEvents = splitAroundSuspends(o, pumpSuspends, firstResumeTime, suspendedPrior, lastSuspendTime, currentlySuspended);
                    splitHistory.AddRange(splitEvents);
                }

                var zTempSuspendBasals = new List<TempHistory>();

                // Any existing temp basals during times the pump was suspended are now deleted
                // Add 0 temp basals to negate the profile basal rates during times pump is suspended
                foreach (var o in pumpSuspends)
                {
                    var zTempBasal = new TempHistory
                    {
                        _type = "SuspendBasal",
                        rate = 0,
                        duration = o.duration,
                        date = o.date,
                        started_at = o.started_at
                    };
                    zTempSuspendBasals.Add(zTempBasal);
                }

                // Add temp suspend basal for maximum DIA (8) up to the resume time
                // if there is no matching suspend in the history before the first
                // resume
                var max_dia_ago = now.ToUnixTimeMilliseconds() - 8 * 60 * 60 * 1000;
                var firstResumeStarted = DateTimeOffset.FromUnixTimeMilliseconds(firstResumeTime).ToLocalTime();
                var firstResumeDate = firstResumeStarted.ToUnixTimeMilliseconds();

                // impact on IOB only matters if the resume occurred
                // after DIA hours before now.
                // otherwise, first resume date can be ignored. Whatever
                // insulin is present prior to resume will be aged
                // out due to DIA.
                if (suspendedPrior && (max_dia_ago < firstResumeDate))
                {
                    var suspendStart = DateTimeOffset.FromUnixTimeMilliseconds(max_dia_ago).ToLocalTime();
                    var suspendStartDate = suspendStart.ToUnixTimeMilliseconds();
                    var started_at = suspendStart;

                    var zTempBasal = new TempHistory
                    {
                        // add _type to aid debugging. It isn't used
                        // anywhere.
                        _type = "SuspendBasal",
                        rate = 0,
                        duration = (firstResumeDate - max_dia_ago) / 60d / 1000,
                        date = suspendStartDate,
                        started_at = started_at
                    };
                    zTempSuspendBasals.Add(zTempBasal);
                }

                if (currentlySuspended)
                {
                    var suspendStart = DateTimeOffset.FromUnixTimeMilliseconds(lastSuspendTime).ToLocalTime();
                    var suspendStartDate = suspendStart.ToUnixTimeMilliseconds();
                    var started_at = suspendStart;

                    var zTempBasal = new TempHistory
                    {
                        _type = "SuspendBasal",
                        rate = 0,
                        duration = (now.ToUnixTimeMilliseconds() - suspendStartDate) / 60d / 1000,
                        date = suspendStartDate,
                        //timestamp = lastSuspendTime,
                        started_at = started_at
                    };
                    zTempSuspendBasals.Add(zTempBasal);
                }

                // Add the new 0 temp basals to the splitHistory.
                // We have to split the new zero temp basals by the profile
                // basals just like the other temp basals.
                foreach (var o in zTempSuspendBasals)
                {
                    splitHistory.AddRange(splitTimespan(o, splitterEvents));
                }
            }
            else
            {
                splitHistory = splitHistoryByBasal;
            }

            splitHistory = splitHistory.OrderBy(x => x.date).ToList();

            // tempHistory = splitHistory;

            // iterate through the temp basals and create bolus events from temps that affect IOB

            for (int i = 0; i < splitHistory.Count; i++)
            {
                var currentItem = splitHistory[i];

                if (currentItem.duration > 0)
                {
                    double currentRate = profile_data.current_basal;
                    if (profile_data.basalprofile.Count > 0)
                    {
                        currentRate = basal.basalLookup(profile_data.basalprofile, currentItem.timestamp.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(currentItem.timestamp.Value).ToLocalTime() : (DateTimeOffset?)null); // WTF
                    }

                    int target_bg = 0;
                    if (profile_data.min_bg is { } min_bg && profile_data.max_bg is { } max_bg)
                    {
                        target_bg = (min_bg + max_bg) / 2;
                    }
                    //if (profile_data.temptargetSet && target_bg > 110) {
                    //sensitivityRatio = 2/(2+(target_bg-100)/40);
                    //currentRate = profile_data.current_basal * sensitivityRatio;
                    //}
                    var profile = profile_data;
                    var normalTarget = 100; // evaluate high/low temptarget against 100, not scheduled basal (which might change)
                    int halfBasalTarget;
                    if (profile.half_basal_exercise_target > 0)
                    {
                        halfBasalTarget = profile.half_basal_exercise_target;
                    }
                    else
                    {
                        halfBasalTarget = 160; // when temptarget is 160 mg/dL, run 50% basal (120 = 75%; 140 = 60%)
                    }

                    double sensitivityRatio = 0;
                    if (profile.exercise_mode && profile.temptargetSet && target_bg >= normalTarget + 5)
                    {
                        // w/ target 100, temp target 110 = .89, 120 = 0.8, 140 = 0.67, 160 = .57, and 200 = .44
                        // e.g.: Sensitivity ratio set to 0.8 based on temp target of 120; Adjusting basal from 1.65 to 1.35; ISF from 58.9 to 73.6
                        var c = halfBasalTarget - normalTarget;
                        sensitivityRatio = (double)c / (c + target_bg - normalTarget);
                    }
                    else if (autosens_data != null)
                    {
                        sensitivityRatio = autosens_data.ratio;
                        //process.stderr.write("Autosens ratio: "+sensitivityRatio+"; ");
                    }

                    if (sensitivityRatio > 0)
                    {
                        currentRate *= sensitivityRatio;
                    }

                    var netBasalRate = (currentItem.rate - currentRate) ?? double.NaN; // WTF: emulating JS
                    var tempBolusSize = netBasalRate < 0 ? -0.05 : 0.05;
                    var netBasalAmount = JSMath.Round(netBasalRate * currentItem.duration * 10 / 6) / 100; // suspect
                    var tempBolusCount = JSMath.Round(netBasalAmount / tempBolusSize);
                    var tempBolusSpacing = currentItem.duration / tempBolusCount;
                    for (int j = 0; j < tempBolusCount; j++)
                    {
                        var tempBolus = new TempBolus();
                        tempBolus.insulin = tempBolusSize;
                        tempBolus.date = (long)(currentItem.date + j * tempBolusSpacing * 60 * 1000);
                        tempBolus.started_at = DateTimeOffset.FromUnixTimeMilliseconds(tempBolus.date).ToLocalTime(); // WTF: was created_at
                        tempBoluses.Add(tempBolus);
                    }
                }
            }

            return tempBoluses.Cast<TempData>().Concat(tempHistory).OrderBy(x => x.date).ToList();
        }
    }

    class SplitterEvent
    {
        public string type { get; set; }
        public int minutes { get; set; }
    }

    class TempData
    {
        public DateTimeOffset started_at { get; set; }
        public long date { get; set; }
    }

    class TempBolus : TempData
    {
        public DateTimeOffset timestamp { get; set; }
        public double? insulin { get; set; }
    }

    class TempHistory : TempData
    {
        public string _type { get; set; }
        /// <summary>
        /// Duration in minutes
        /// </summary>
        public double duration { get; set; }
        public double? rate; // WTF: Nullable to emulate undefined
        public long? timestamp { get; set; }

        public TempHistory Clone()
        {
            return new TempHistory
            {
                _type = _type,
                date = date,
                duration = duration,
                rate = rate,
                started_at = started_at,
                timestamp = timestamp
            };
        }
    }

    class InsulinRecord
    {
        public long? timestamp { get; set; }
        public DateTimeOffset started_at { get; set; }
        public long date { get; set; }
        public long duration { get; set; }
    }
}
