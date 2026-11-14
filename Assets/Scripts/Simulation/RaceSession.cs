using System;

namespace DragRace.Simulation
{
    public enum RacePhase { Staging, Countdown, Racing, Finished }

    /// <summary>
    /// One two-lane race: countdown, automatic launch, and a winner decided by who crosses first.
    /// The presentation layer calls <see cref="Advance"/> each frame and reads the two cars.
    /// </summary>
    public class RaceSession
    {
        public const float QuarterMileM = 402.336f;

        public readonly DragCarSim Player;
        public readonly DragCarSim Opponent;
        public readonly AiShifter OpponentDriver;

        public RacePhase Phase { get; private set; }
        public float CountdownRemaining { get; private set; }
        /// <summary>Identifier that changes on every reset; used to pay each race only once.</summary>
        public string RaceId { get; private set; }

        public float countdownSeconds = 3f;

        public RaceSession(CarSpec playerSpec, CarSpec opponentSpec, AiShifter opponentDriver, float distanceM = QuarterMileM)
        {
            Player = new DragCarSim(playerSpec, distanceM);
            Opponent = new DragCarSim(opponentSpec, distanceM);
            OpponentDriver = opponentDriver;
            Reset();
        }

        /// <summary>Returns everything to the staged state for a fresh race.</summary>
        public void Reset()
        {
            Player.Reset();
            Opponent.Reset();
            OpponentDriver.Reset();
            Phase = RacePhase.Staging;
            CountdownRemaining = countdownSeconds;
            RaceId = Guid.NewGuid().ToString("N");
        }

        public void StartCountdown()
        {
            if (Phase != RacePhase.Staging) return;
            CountdownRemaining = countdownSeconds;
            Phase = RacePhase.Countdown;
        }

        public ShiftQuality PlayerShift()
        {
            return Phase == RacePhase.Racing ? Player.RequestShift() : ShiftQuality.None;
        }

        public void Advance(float deltaTime)
        {
            if (Phase == RacePhase.Countdown)
            {
                CountdownRemaining -= deltaTime;
                if (CountdownRemaining > 0f) return;
                deltaTime = -CountdownRemaining;   // carry the leftover into the race
                CountdownRemaining = 0f;
                Player.Launch();
                Opponent.Launch();
                Phase = RacePhase.Racing;
            }

            if (Phase != RacePhase.Racing) return;

            float remaining = deltaTime;
            while (remaining > 0f)
            {
                float dt = Math.Min(remaining, DragCarSim.FixedStep);
                OpponentDriver.Update(Opponent, dt);
                Player.Advance(dt);
                Opponent.Advance(dt);
                remaining -= dt;
            }

            if (Player.Finished && Opponent.Finished) Phase = RacePhase.Finished;
        }

        public bool PlayerWon { get { return Player.FinishTime <= Opponent.FinishTime; } }
        /// <summary>Seconds between the two finish-line crossings.</summary>
        public float MarginSeconds { get { return Math.Abs(Player.FinishTime - Opponent.FinishTime); } }
    }
}
