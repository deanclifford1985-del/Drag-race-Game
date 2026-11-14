using System;

namespace DragRace.Simulation
{
    /// <summary>
    /// All mechanical properties of one car, in SI units (kg, m, N, Nm, s) with engine speed in RPM.
    /// Plain data so upgrades can later modify a copy of the spec before a race.
    /// </summary>
    [Serializable]
    public class CarSpec
    {
        public string displayName = "Car";

        // Engine
        /// <summary>Torque curve sample points, ascending RPM.</summary>
        public float[] torqueCurveRpm = { 1000, 2000, 3000, 4000, 4800, 5600, 6200, 6600, 7000, 7400, 7800 };
        /// <summary>Flywheel torque in Nm at each sample point.</summary>
        public float[] torqueCurveNm = { 240, 300, 350, 385, 400, 390, 365, 325, 265, 205, 150 };
        public float idleRpm = 900f;
        /// <summary>Start of the red zone on the tachometer.</summary>
        public float redlineRpm = 7000f;
        /// <summary>RPM at which the limiter cuts engine torque.</summary>
        public float limiterRpm = 7500f;
        /// <summary>Clutch-slip RPM held off the line until the car's speed catches up.</summary>
        public float launchRpm = 4200f;
        /// <summary>Engine braking torque (Nm) applied while the limiter is cutting.</summary>
        public float limiterDragNm = 40f;

        // Drivetrain
        public float[] gearRatios = { 3.20f, 2.10f, 1.50f, 1.15f, 0.92f };
        public float finalDrive = 3.90f;
        public float drivetrainEfficiency = 0.88f;
        /// <summary>Seconds of torque interruption on each upshift.</summary>
        public float shiftDuration = 0.22f;

        // Chassis
        public float massKg = 1450f;
        public float tireRadiusM = 0.33f;
        /// <summary>Peak tire friction coefficient on the driven axle.</summary>
        public float tireGrip = 1.15f;
        /// <summary>Share of static weight on the driven (rear) axle.</summary>
        public float drivenWeightFraction = 0.50f;
        public float cgHeightM = 0.50f;
        public float wheelbaseM = 2.65f;

        // Resistance
        /// <summary>Drag coefficient times frontal area, m^2.</summary>
        public float dragAreaCdA = 0.68f;
        public float rollingResistance = 0.014f;

        // Shift feedback windows, RPM either side of the optimal shift point
        public float perfectWindowRpm = 130f;
        public float goodWindowRpm = 420f;

        public int GearCount { get { return gearRatios.Length; } }

        /// <summary>Flywheel torque at an engine speed, linearly interpolated and clamped at the ends.</summary>
        public float TorqueAt(float rpm)
        {
            float[] x = torqueCurveRpm, y = torqueCurveNm;
            if (rpm <= x[0]) return y[0];
            int last = x.Length - 1;
            if (rpm >= x[last]) return y[last];
            for (int i = 1; i <= last; i++)
            {
                if (rpm <= x[i])
                {
                    float t = (rpm - x[i - 1]) / (x[i] - x[i - 1]);
                    return y[i - 1] + (y[i] - y[i - 1]) * t;
                }
            }
            return y[last];
        }

        /// <summary>Engine RPM when rigidly coupled to the wheels at a road speed in a gear (1-based).</summary>
        public float CoupledRpm(float speedMs, int gear)
        {
            float wheelRadPerSec = speedMs / tireRadiusM;
            return wheelRadPerSec * gearRatios[gear - 1] * finalDrive * 60f / (2f * (float)Math.PI);
        }

        /// <summary>
        /// The RPM at which shifting out of <paramref name="gear"/> gives the most acceleration:
        /// the lowest RPM where the next gear would put at least as much force to the road.
        /// Falls back to just under the limiter when the current gear is always stronger.
        /// </summary>
        public float OptimalShiftRpm(int gear)
        {
            float cap = limiterRpm - 60f;
            if (gear >= GearCount) return cap;
            float ratio = gearRatios[gear - 1], next = gearRatios[gear];
            for (float rpm = launchRpm; rpm <= cap; rpm += 10f)
            {
                float forceNow = TorqueAt(rpm) * ratio;
                float forceNext = TorqueAt(rpm * next / ratio) * next;
                if (forceNext >= forceNow) return rpm;
            }
            return cap;
        }

        public CarSpec Clone()
        {
            var c = (CarSpec)MemberwiseClone();
            c.torqueCurveRpm = (float[])torqueCurveRpm.Clone();
            c.torqueCurveNm = (float[])torqueCurveNm.Clone();
            c.gearRatios = (float[])gearRatios.Clone();
            return c;
        }
    }
}
