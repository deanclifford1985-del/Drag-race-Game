using System;
using System.Collections.Generic;
using DragRace.Simulation;

// Headless checks for the race simulation. Run with: dotnet run --project SimTests
static class Program
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) failures++;
    }

    /// <summary>Runs one car alone, shifting at optimal + offset RPM (or holding the limiter for holdSeconds).</summary>
    static DragCarSim Solo(CarSpec spec, float offsetRpm, float limiterHold = 0f, bool neverShift = false)
    {
        var car = new DragCarSim(spec, RaceSession.QuarterMileM);
        car.Launch();
        float held = 0f;
        while (!car.Finished && car.Time < 60f)
        {
            if (!neverShift && !car.IsShifting && car.Gear < spec.GearCount)
            {
                if (limiterHold > 0f)
                {
                    if (car.OnLimiter) { held += DragCarSim.FixedStep; if (held >= limiterHold) { car.RequestShift(); held = 0f; } }
                }
                else if (car.Rpm >= car.OptimalShiftRpm + offsetRpm) car.RequestShift();
            }
            car.Advance(DragCarSim.FixedStep);
        }
        return car;
    }

    class MemoryStore : IKeyValueStore
    {
        readonly Dictionary<string, object> d = new Dictionary<string, object>();
        public int GetInt(string k, int f) { return d.TryGetValue(k, out var v) ? (int)v : f; }
        public void SetInt(string k, int v) { d[k] = v; }
        public string GetString(string k, string f) { return d.TryGetValue(k, out var v) ? (string)v : f; }
        public void SetString(string k, string v) { d[k] = v; }
        public void Save() { }
    }

    static int Main()
    {
        var spec = new CarSpec();
        for (int g = 1; g < spec.GearCount; g++)
            Console.WriteLine($"gear {g} optimal shift {spec.OptimalShiftRpm(g):0} rpm");

        var perfect = Solo(spec, 0f);
        var early = Solo(spec, -1500f);
        var veryEarly = Solo(spec, -2500f);
        var late = Solo(spec, 0f, limiterHold: 0.5f);
        var never = Solo(spec, 0f, neverShift: true);
        Console.WriteLine($"perfect    {perfect.FinishTime:0.000}s @ {perfect.FinishSpeedMs * 2.23694f:0.0} mph, gear {perfect.Gear}");
        Console.WriteLine($"early      {early.FinishTime:0.000}s");
        Console.WriteLine($"very early {veryEarly.FinishTime:0.000}s");
        Console.WriteLine($"late       {late.FinishTime:0.000}s");
        Console.WriteLine($"never      {never.FinishTime:0.000}s finished={never.Finished}");

        Check(perfect.Finished && perfect.FinishTime > 10.5f && perfect.FinishTime < 14.5f, "perfect run gives a plausible quarter-mile time");
        Check(early.FinishTime > perfect.FinishTime + 0.15f, "early shifts cost time");
        Check(veryEarly.FinishTime > early.FinishTime, "earlier shifts cost more");
        Check(late.FinishTime > perfect.FinishTime + 0.15f, "riding the limiter costs time");
        Check(!never.Finished || never.FinishTime > perfect.FinishTime + 2f, "never shifting is far slower");

        // Small offsets either side of optimal must not beat optimal.
        bool optimalIsBest = true;
        foreach (float off in new[] { -600f, -300f, 150f })
            if (Solo(spec, off).FinishTime < perfect.FinishTime - 0.002f) optimalIsBest = false;
        Check(optimalIsBest, "no nearby shift point beats the computed optimum");

        // Shift quality labels.
        var c = new DragCarSim(spec, 402f); c.Launch();
        while (c.Rpm < c.OptimalShiftRpm - 1500f || c.SpeedMs < 5f) c.Advance(DragCarSim.FixedStep);
        Check(c.RequestShift() == ShiftQuality.Early, "shift far below optimal is Early");
        c.Reset(); c.Launch();
        while (c.Rpm < c.OptimalShiftRpm - 30f || c.SpeedMs < 5f) c.Advance(DragCarSim.FixedStep);
        Check(c.RequestShift() == ShiftQuality.Perfect, "shift at optimal is Perfect");
        Check(c.RequestShift() == ShiftQuality.None, "shift during a shift is ignored");
        c.Reset(); c.Launch();
        while (!c.OnLimiter) c.Advance(DragCarSim.FixedStep);
        Check(c.RequestShift() == ShiftQuality.Late, "shift on the limiter is Late");
        c.Reset();
        Check(c.PositionM == 0f && c.Gear == 1 && !c.Launched && !c.Finished && c.Time == 0f, "reset restores the staged state");

        // Player versus AI over many seeds.
        int perfectWins = 0, sloppyWins = 0, races = 200;
        float aiMin = 99f, aiMax = 0f;
        for (int seed = 0; seed < races; seed++)
        {
            foreach (bool sloppy in new[] { false, true })
            {
                var race = new RaceSession(spec, spec.Clone(), new AiShifter(seed));
                race.StartCountdown();
                while (race.Phase != RacePhase.Finished && race.Player.Time < 60f)
                {
                    var p = race.Player;
                    float offset = sloppy ? -1300f : 0f;
                    if (race.Phase == RacePhase.Racing && p.Rpm >= p.OptimalShiftRpm + offset) race.PlayerShift();
                    race.Advance(1f / 60f);
                }
                if (race.PlayerWon) { if (sloppy) sloppyWins++; else perfectWins++; }
                aiMin = Math.Min(aiMin, race.Opponent.FinishTime);
                aiMax = Math.Max(aiMax, race.Opponent.FinishTime);
            }
        }
        Console.WriteLine($"AI times {aiMin:0.000}-{aiMax:0.000}s; perfect player won {perfectWins}/{races}, sloppy player won {sloppyWins}/{races}");
        Check(perfectWins >= races * 0.9, "well-timed shifting beats the AI at least 90% of the time");
        Check(sloppyWins <= races * 0.1, "badly early shifting loses to the AI at least 90% of the time");

        // Frame-rate independence.
        float At(float frame)
        {
            var race = new RaceSession(spec, spec.Clone(), new AiShifter(7));
            race.StartCountdown();
            while (race.Phase != RacePhase.Finished) race.Advance(frame);
            return race.Opponent.FinishTime;
        }
        Check(Math.Abs(At(1f / 30f) - At(1f / 144f)) < 0.02f, "AI finish time is stable across frame rates");

        // Rewards.
        var wallet = new Wallet(new MemoryStore());
        var r = new RaceSession(spec, spec.Clone(), new AiShifter(1));
        Check(wallet.TryAward(r.RaceId, 500) && wallet.Cash == 500, "first award pays");
        Check(!wallet.TryAward(r.RaceId, 500) && wallet.Cash == 500, "second award for the same race is refused");
        r.Reset();
        Check(wallet.TryAward(r.RaceId, 500) && wallet.Cash == 1000, "a new race pays again");

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
