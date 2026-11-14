using System;

namespace DragRace.Simulation
{
    public enum ShiftQuality { None, Early, Good, Perfect, Late }

    /// <summary>
    /// Longitudinal simulation of one drag car. Player and AI cars both run this class;
    /// the only driver input is <see cref="RequestShift"/>.
    /// Call <see cref="Advance"/> with any frame time; it sub-steps at a fixed rate.
    /// </summary>
    public class DragCarSim
    {
        public const float Gravity = 9.81f;
        public const float AirDensity = 1.225f;
        public const float FixedStep = 1f / 500f;

        public readonly CarSpec Spec;

        public float Time { get; private set; }
        public float PositionM { get; private set; }
        public float SpeedMs { get; private set; }
        public float AccelMs2 { get; private set; }
        public float Rpm { get; private set; }
        public int Gear { get; private set; }
        public bool Launched { get; private set; }
        public bool IsShifting { get { return shiftTimer > 0f; } }
        public bool OnLimiter { get; private set; }
        /// <summary>True while the tires are at their grip limit (use for wheelspin effects).</summary>
        public bool TractionLimited { get; private set; }

        public bool Finished { get; private set; }
        public float FinishTime { get; private set; }
        public float FinishSpeedMs { get; private set; }
        public float RaceDistanceM { get; private set; }

        public ShiftQuality LastShiftQuality { get; private set; }
        /// <summary>RPM error of the last shift relative to the optimal point (negative = early).</summary>
        public float LastShiftErrorRpm { get; private set; }

        float shiftTimer;
        float accumulator;

        public DragCarSim(CarSpec spec, float raceDistanceM)
        {
            Spec = spec;
            RaceDistanceM = raceDistanceM;
            Reset();
        }

        /// <summary>Returns the car to the start line, staged in first gear and holding launch RPM.</summary>
        public void Reset()
        {
            Time = 0f; PositionM = 0f; SpeedMs = 0f; AccelMs2 = 0f;
            Gear = 1; Launched = false; shiftTimer = 0f; accumulator = 0f;
            OnLimiter = false; TractionLimited = false;
            Finished = false; FinishTime = 0f; FinishSpeedMs = 0f;
            LastShiftQuality = ShiftQuality.None; LastShiftErrorRpm = 0f;
            Rpm = Spec.launchRpm;
        }

        public void Launch() { Launched = true; }

        /// <summary>RPM band for shift feedback in the current gear.</summary>
        public float OptimalShiftRpm { get { return Spec.OptimalShiftRpm(Gear); } }

        /// <summary>
        /// Upshift now. Returns the quality of the timing, or None when the shift is not possible
        /// (not launched, already shifting, top gear, or finished).
        /// </summary>
        public ShiftQuality RequestShift()
        {
            if (!Launched || Finished || IsShifting || Gear >= Spec.GearCount) return ShiftQuality.None;

            float error = Rpm - Spec.OptimalShiftRpm(Gear);
            ShiftQuality q;
            if (OnLimiter) q = ShiftQuality.Late;
            else if (Math.Abs(error) <= Spec.perfectWindowRpm) q = ShiftQuality.Perfect;
            else if (Math.Abs(error) <= Spec.goodWindowRpm) q = ShiftQuality.Good;
            else q = error < 0f ? ShiftQuality.Early : ShiftQuality.Late;

            LastShiftQuality = q;
            LastShiftErrorRpm = error;
            Gear++;
            shiftTimer = Spec.shiftDuration;
            return q;
        }

        /// <summary>Advance by a variable frame time using fixed sub-steps.</summary>
        public void Advance(float deltaTime)
        {
            accumulator += deltaTime;
            while (accumulator >= FixedStep)
            {
                Step(FixedStep);
                accumulator -= FixedStep;
            }
        }

        void Step(float dt)
        {
            if (!Launched) { Rpm = Spec.launchRpm; return; }

            CarSpec s = Spec;
            float coupled = s.CoupledRpm(SpeedMs, Gear);

            // Clutch slip off the line in first gear; otherwise the engine follows the wheels.
            float engineRpm = coupled;
            if (Gear == 1 && coupled < s.launchRpm) engineRpm = s.launchRpm;
            if (engineRpm < s.idleRpm) engineRpm = s.idleRpm;

            float overall = s.gearRatios[Gear - 1] * s.finalDrive;
            float engineTorque;
            OnLimiter = false;
            if (shiftTimer > 0f)
            {
                engineTorque = 0f;              // torque interruption while the gear change completes
                shiftTimer -= dt;
            }
            else if (engineRpm >= s.limiterRpm)
            {
                engineTorque = -s.limiterDragNm; // limiter cuts fuel
                OnLimiter = true;
            }
            else
            {
                engineTorque = s.TorqueAt(engineRpm);
            }

            float driveForce = engineTorque * overall * s.drivetrainEfficiency / s.tireRadiusM;

            // Grip limit on the driven rear axle, including weight transfer under acceleration:
            // F <= mu * (m g frac + F h / L)  =>  F <= mu m g frac / (1 - mu h / L)
            float gripLimit = s.tireGrip * s.massKg * Gravity * s.drivenWeightFraction
                              / (1f - s.tireGrip * s.cgHeightM / s.wheelbaseM);
            TractionLimited = driveForce > gripLimit;
            if (TractionLimited) driveForce = gripLimit;

            float aero = 0.5f * AirDensity * s.dragAreaCdA * SpeedMs * SpeedMs;
            float rolling = SpeedMs > 0.05f ? s.rollingResistance * s.massKg * Gravity : 0f;

            float accel = (driveForce - aero - rolling) / s.massKg;
            float newSpeed = SpeedMs + accel * dt;
            if (newSpeed < 0f) { newSpeed = 0f; accel = 0f; }

            float newPos = PositionM + 0.5f * (SpeedMs + newSpeed) * dt;

            if (!Finished && newPos >= RaceDistanceM)
            {
                // Interpolate the exact crossing inside this step.
                float frac = (RaceDistanceM - PositionM) / Math.Max(newPos - PositionM, 1e-6f);
                FinishTime = Time + frac * dt;
                FinishSpeedMs = SpeedMs + (newSpeed - SpeedMs) * frac;
                Finished = true;
            }

            PositionM = newPos;
            SpeedMs = newSpeed;
            AccelMs2 = accel;
            Time += dt;

            // Tachometer reading: smooth toward the mechanical RPM so shifts read as a quick drop.
            float target = Math.Min(engineRpm, s.limiterRpm);
            float rate = 14000f * dt;
            Rpm = Math.Abs(target - Rpm) <= rate ? target : Rpm + Math.Sign(target - Rpm) * rate;
        }
    }
}
