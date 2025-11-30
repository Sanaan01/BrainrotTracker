using System;
using System.Linq;
using System.Threading;
using Brainrot.Core;

namespace Brainrot.TrackerConsole
{
    internal class Program
    {
        static void Main()
        {
            var tracker = new BrainrotTracker();

            Console.Clear();
            Console.WriteLine("Brainrot tracker – per-app usage (seconds)");
            Console.WriteLine("Press Ctrl+C to exit.");
            Console.WriteLine();

            RenderSnapshot(tracker);

            int tick = 0;

            while (true)
            {
                tracker.Tick();
                tick++;

                if (tick % 5 == 0)
                {
                    RenderSnapshot(tracker);
                }

                Thread.Sleep(1000);
            }
        }

        private static void RenderSnapshot(BrainrotTracker tracker)
        {
            Console.Clear();
            Console.WriteLine("Brainrot tracker – per-app usage (seconds)");
            Console.WriteLine("Press Ctrl+C to exit.");
            Console.WriteLine();

            var snapshot = tracker.GetSnapshot();

            Console.WriteLine($"Rot:     {FormatTime(snapshot.RotSeconds)}");
            Console.WriteLine($"Focus:   {FormatTime(snapshot.FocusSeconds)}");
            Console.WriteLine($"Neutral: {FormatTime(snapshot.NeutralSeconds)}");
            Console.WriteLine();
            Console.WriteLine("Per-app usage (top few):");

            foreach (var kvp in snapshot.PerAppSeconds
                         .OrderByDescending(kvp => kvp.Value)
                         .Take(10))
            {
                Console.WriteLine($"- {kvp.Key,-20} {FormatTime(kvp.Value)}");
            }
        }

        private static string FormatTime(int seconds)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m}m {s:00}s";
        }
    }
}
