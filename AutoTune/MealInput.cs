using System;

namespace AutoTune
{
    class MealInput
    {
        public int? carbs { get; set; }
        public int? nsCarbs { get; set; }
        public DateTimeOffset timestamp { get; set; }
        public double? bolus { get; set; }
    }
}