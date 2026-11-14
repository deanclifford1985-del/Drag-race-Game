using System;

namespace DragRace.Simulation
{
    /// <summary>
    /// Drives a <see cref="DragCarSim"/> by shifting near the optimal point with a configurable
    /// random error. Uses the same shift request as the player, so it obeys the same physics.
    /// </summary>
    public class AiShifter
    {
        /// <summary>Standard deviation of the shift-point error, in RPM. 0 = perfect every time.</summary>
        public float errorStdDevRpm = 260f;
        /// <summary>Constant offset, in RPM. Negative makes the AI habitually shift early.</summary>
        public float biasRpm = -120f;

        readonly Random rng;
        int plannedGear;
        float targetRpm;
        float limiterHold;

        public AiShifter(int seed) { rng = new Random(seed); }

        public void Reset() { plannedGear = 0; }

        public void Update(DragCarSim car, float deltaTime)
        {
            if (!car.Launched || car.Finished || car.IsShifting || car.Gear >= car.Spec.GearCount) return;

            if (plannedGear != car.Gear)
            {
                plannedGear = car.Gear;
                float wanted = car.OptimalShiftRpm + biasRpm + Gaussian() * errorStdDevRpm;
                float cap = car.Spec.limiterRpm - 20f;
                // A target past the limiter becomes time spent bouncing on it.
                limiterHold = wanted > cap ? (wanted - cap) / 2000f : 0f;
                targetRpm = Math.Min(wanted, cap);
            }

            if (car.Rpm < targetRpm) return;
            if (limiterHold > 0f) { limiterHold -= deltaTime; return; }
            car.RequestShift();
        }

        float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
