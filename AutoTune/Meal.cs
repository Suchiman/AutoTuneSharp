using System;
using System.Collections.Generic;

namespace AutoTune
{
    static class Meal
    {
        static bool arrayHasElementWithSameTimestampAndProperty(List<MealInput> array, DateTimeOffset t, Predicate<MealInput> propname)
        {
            for (var j = 0; j < array.Count; j++)
            {
                var element = array[j];
                if (Math.Abs((element.timestamp - t).TotalSeconds) < 2 && propname(element)) return true;
            }
            return false;
        }

        public static List<MealInput> findMealInputs(Prepare inputs)
        {
            var pumpHistory = inputs.history;
            var carbHistory = inputs.carbs;
            var profile_data = inputs.profile;
            var mealInputs = new List<MealInput>();
            var bolusWizardInputs = new List<Treatment>();
            var duplicates = 0;

            for (var i = 0; i < carbHistory?.Count; i++)
            {
                throw new NotImplementedException("carbs");
                //var current = carbHistory[i];
                //if (current.carbs && current.created_at)
                //{
                //    var temp = new MealInput();
                //    temp.timestamp = current.created_at;
                //    temp.carbs = current.carbs;
                //    temp.nsCarbs = current.carbs;
                //    if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.created_at, x => x.carbs != null))
                //    {
                //        mealInputs.Add(temp);
                //    }
                //    else
                //    {
                //        duplicates += 1;
                //    }
                //}
            }

            for (int i = 0; i < pumpHistory.Count; i++)
            {
                var current = pumpHistory[i];
                if (current._type == "Bolus" && current.timestamp != null)
                {
                    throw new NotImplementedException("_type == Bolus");
                    //console.log(pumpHistory[i]);
                    //var temp = new MealInput();
                    //temp.timestamp = current.timestamp;
                    //temp.bolus = current.amount;

                    //if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.timestamp, "bolus"))
                    //{
                    //    mealInputs.Add(temp);
                    //}
                    //else
                    //{
                    //    duplicates += 1;
                    //}
                }
                else if (current._type == "BolusWizard" && current.timestamp != null)
                {
                    throw new NotImplementedException("_type == BolusWizard");
                    // Delay process the BolusWizard entries to make sure we've seen all possible that correspond to the bolus wizard.
                    // More specifically, we need to make sure we process the corresponding bolus entry first.
                    //bolusWizardInputs.Add(current);
                }
                else if ((current._type == "Meal Bolus" || current._type == "Correction Bolus" || current._type == "Snack Bolus" || current._type == "Bolus Wizard" || current._type == "Carb Correction") && current.created_at > DateTimeOffset.MinValue) // WTF: why does this check _type but History.find_insulin checks eventType
                {
                    //imports carbs entered through Nightscout Care Portal
                    //"Bolus Wizard" refers to the Nightscout Bolus Wizard, not the Medtronic Bolus Wizard
                    var temp = new MealInput();
                    temp.timestamp = current.created_at;
                    temp.carbs = current.carbs;
                    temp.nsCarbs = current.carbs;
                    // don't enter the treatment if there's another treatment with the same exact timestamp
                    // to prevent duped carb entries from multiple sources
                    if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.created_at, x => x.carbs != null))
                    {
                        mealInputs.Add(temp);
                    }
                    else
                    {
                        duplicates += 1;
                    }
                }
                else if (current.enteredBy == "xdrip")
                {
                    var temp = new MealInput();
                    temp.timestamp = current.created_at;
                    temp.carbs = current.carbs;
                    temp.nsCarbs = current.carbs;
                    temp.bolus = current.insulin;
                    if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.created_at, x => x.carbs != null)) // instead of timestamp
                    {
                        mealInputs.Add(temp);
                    }
                    else
                    {
                        duplicates += 1;
                    }
                }
                else if (current.carbs > 0)
                {
                    var temp = new MealInput();
                    temp.carbs = current.carbs;
                    temp.nsCarbs = current.carbs;
                    temp.timestamp = current.created_at;
                    temp.bolus = current.insulin;
                    if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.created_at, x => x.carbs != null)) // instead of timestamp
                    {
                        mealInputs.Add(temp);
                    }
                    else
                    {
                        duplicates += 1;
                    }
                }
                else if (current._type == "JournalEntryMealMarker" /*&& current.carb_input > 0 && current.timestamp*/)
                {
                    throw new NotImplementedException("_type == JournalEntryMealMarker");
                    //var temp = new MealInput();
                    //temp.timestamp = DateTimeOffset.FromUnixTimeMilliseconds(current.timestamp.Value);
                    //temp.carbs = current.carb_input;
                    //temp.journalCarbs = current.carb_input;
                    //if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.timestamp, "carbs"))
                    //{
                    //    mealInputs.Add(temp);
                    //}
                    //else
                    //{
                    //    duplicates += 1;
                    //}
                }
            }

            //for (int i = 0; i < bolusWizardInputs.Count; i++)
            //{
            //    var current = bolusWizardInputs[i];
            //    //console.log(bolusWizardInputs[i]);
            //    var temp = new MealInput();
            //    temp.timestamp = current.timestamp;
            //    temp.carbs = current.carb_input;
            //    temp.bwCarbs = current.carb_input;

            //    // don't enter the treatment if there's another treatment with the same exact timestamp
            //    // to prevent duped carb entries from multiple sources
            //    if (!arrayHasElementWithSameTimestampAndProperty(mealInputs, current.timestamp, "carbs"))
            //    {
            //        if (arrayHasElementWithSameTimestampAndProperty(mealInputs, current.timestamp, "bolus"))
            //        {
            //            mealInputs.Add(temp);
            //            //bwCarbs += temp.carbs;
            //        }
            //        else
            //        {
            //            Console.WriteLine($"Skipping bolus wizard entry {i} in the pump history with {current.carb_input}g carbs and no insulin.");
            //            if (current.carb_input == 0)
            //            {
            //                Console.WriteLine("This is caused by a BolusWizard without carbs. If you specified insulin, it will be noted as a seperate Bolus");
            //            }
            //            if (current.timestamp)
            //            {
            //                Console.WriteLine("Timestamp of bolus wizard: " + current.timestamp);
            //            }
            //        }
            //    }
            //    else
            //    {
            //        duplicates += 1;
            //    }
            //}
            if (duplicates > 0) Console.WriteLine("Removed duplicate bolus/carb entries: " + duplicates);

            return mealInputs;
        }
    }
}
