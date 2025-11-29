using System;
using System.Linq;
using System.Threading;
using Brainrot.Core;

namespace Brainrot.Console
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var tracker = new BrainrotTracker();

            System.Console.WriteLine("Brainrot tracker – per-app usage (seconds)");
            System.Console.WriteLine("Press Ctrl+C to exit.");
            System.Console.WriteLine();

            int tick = 0;

            while (true)
            {
                tracker.Tick();
                tick++;

                if (tick % 5 == 0)
                {
                    PrintSnapshot(tracker.GetSnapshot());
                }

                Thread.Sleep(1000);
            }
        }

        private static void PrintSnapshot(BrainUsageSnapshot snapshot)
        {
            var topApps = snapshot.PerAppSeconds
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToList();

            System.Console.Clear();
            System.Console.WriteLine("Brainrot tracker – per-app usage (seconds)");
            System.Console.WriteLine("Press Ctrl+C to exit.");
            System.Console.WriteLine();

            System.Console.WriteLine($"Rot:     {FormatTime(snapshot.RotSeconds)}");
            System.Console.WriteLine($"Focus:   {FormatTime(snapshot.FocusSeconds)}");
            System.Console.WriteLine($"Neutral: {FormatTime(snapshot.NeutralSeconds)}");
            System.Console.WriteLine();

            System.Console.WriteLine("Per-app usage (top):");
            foreach (var kvp in topApps)
            {
                System.Console.WriteLine($"- {kvp.Key,-20} {FormatTime(kvp.Value)}");
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
