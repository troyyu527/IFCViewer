using System;
using System.Globalization;

namespace IfcViewer.Infrastructure
{
    /// <summary>
    /// Formats SI values (meters, m², m³) in imperial units for display:
    /// lengths as feet-inches like Revit (9' - 10 7/64", rounded to 1/256"), areas in ft², volumes in ft³.
    /// </summary>
    public static class UnitFormatter
    {
        private const double MetersPerInch = 0.0254;
        private const double SquareFeetPerSquareMeter = 1 / (0.3048 * 0.3048);
        private const double CubicFeetPerCubicMeter = 1 / (0.3048 * 0.3048 * 0.3048);

        // Smallest fraction of an inch shown.
        private const int Denominator = 256;

        // 3.0 → 9' - 10 7/64"; 0 → 0' - 0"; 0.0127 → 0' - 0 1/2".
        public static string Length(double meters)
        {
            // Work in whole 1/256" steps, so rounding happens once and can't produce 12" or 256/256"
            var steps = (long)Math.Round(Math.Abs(meters) / MetersPerInch * Denominator, MidpointRounding.AwayFromZero);
            var sign = meters < 0 && steps > 0 ? "-" : "";

            var feet = steps / (12 * Denominator);
            var rest = steps % (12 * Denominator);
            var inches = rest / Denominator;
            var fraction = rest % Denominator;

            var text = $"{sign}{feet}' - {inches}";
            if (fraction > 0)
            {
                var divisor = GreatestCommonDivisor(fraction, Denominator);
                text += $" {fraction / divisor}/{Denominator / divisor}";
            }
            return text + "\"";
        }

        public static string Area(double squareMeters) =>
            (squareMeters * SquareFeetPerSquareMeter).ToString("#,##0.00", CultureInfo.InvariantCulture) + " ft²";

        public static string Volume(double cubicMeters) =>
            (cubicMeters * CubicFeetPerCubicMeter).ToString("#,##0.00", CultureInfo.InvariantCulture) + " ft³";

        // Plain numbers without a unit: up to 3 decimals, no trailing zeros.
        public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static long GreatestCommonDivisor(long a, long b)
        {
            while (b != 0)
            {
                var t = a % b;
                a = b;
                b = t;
            }
            return a;
        }
    }
}
