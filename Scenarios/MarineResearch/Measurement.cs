using System;

namespace MarineResearch
{
    public class Measurement
    {
        public const double Difference = .000001;

        public DateTime Time { get; }
        public double Temperature { get; }
        public double Salinity { get; }

        public Measurement(DateTime time, double temperature, double salinity)
        {
            Time = time;
            Temperature = temperature;
            Salinity = salinity;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Measurement other))
                return false;

            return Time.Equals(other.Time) &&
                   Math.Abs(Temperature - other.Temperature) <= Difference &&
                   Math.Abs(Salinity - other.Salinity) <= Difference;
        }

        protected bool Equals(Measurement other)
        {
            return Time.Equals(other.Time) && Temperature.Equals(other.Temperature) && Salinity.Equals(other.Salinity);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Time.GetHashCode();
                hashCode = (hashCode * 397) ^ Temperature.GetHashCode();
                hashCode = (hashCode * 397) ^ Salinity.GetHashCode();
                return hashCode;
            }
        }
    }
}