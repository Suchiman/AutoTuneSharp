using System;

namespace AutoTune
{
    static class JSMath
    {
        private static readonly double[] roundPower10Double = new double[] {
          1E0, 1E1, 1E2, 1E3, 1E4, 1E5, 1E6, 1E7, 1E8,
          1E9, 1E10, 1E11, 1E12, 1E13, 1E14, 1E15
        };

        public static double Round(double value)
        {
            return Math.Floor(value + 0.5);
        }

        public static double Round(double value, int digits)
        {
            double power10 = roundPower10Double[digits];
            return Math.Floor((value * power10) + 0.5) / power10;
        }

        public static decimal ToFixed(double value, int digits = 0)
        {
            if (digits > 20)
            {
                throw new ArgumentException("This implementation does not support more than 20 digits", nameof(digits));
            }

            Span<char> chars = stackalloc char[100];
            if (value.TryFormat(chars, out int written, "F21"))
            {
                chars = chars.Slice(0, written);
            }
            else
            {
                throw new NotImplementedException();
            }
            return Math.Round(Decimal.Parse(chars), digits, MidpointRounding.AwayFromZero);
        }
    }
}
