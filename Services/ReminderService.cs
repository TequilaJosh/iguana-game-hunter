using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameTracker.Models;

namespace GameTracker.Services
{
    /// <summary>
    /// Game reminders: the scheduling rules (shared by the running app and the closed-app
    /// "--reminders" mode) plus the Windows scheduled task that wakes Game Hunter to show a
    /// reminder while the app is closed.
    /// </summary>
    public static class ReminderService
    {
        public enum Choice { GotIt, Snooze, TurnOff }

        public const string WakeArg = "--reminders";
        public const string TaskName = "LazerGuanas Game Hunter reminders";
        public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Reminders due at <paramref name="now"/>. Each one's schedule is advanced in place: the
        /// next one is a full interval from NOW (a reminder that came due several times while
        /// nothing was running shows once, not once per missed interval); a one-time reminder is
        /// finished. Returns the games to show, with the time each was due.
        /// </summary>
        public static List<(Game Game, DateTime DueAt)> TakeDue(IEnumerable<Game> games, DateTime now)
        {
            var due = new List<(Game, DateTime)>();
            foreach (var g in games.Where(g => g.HasReminder && g.Reminder!.NextAt <= now))
            {
                var r = g.Reminder!;
                due.Add((g, r.NextAt));
                if (r.RepeatMinutes > 0) r.NextAt = now.AddMinutes(r.RepeatMinutes);
                else r.Enabled = false;
            }
            return due;
        }

        /// <summary>Apply a pop-up choice. Returns true if the game's reminder changed.</summary>
        public static bool Apply(Game game, Choice choice, DateTime now)
        {
            if (game.Reminder == null) return false;
            switch (choice)
            {
                case Choice.Snooze:
                    game.Reminder.Enabled = true;   // also revives a one-time reminder
                    game.Reminder.NextAt = now + SnoozeFor;
                    return true;
                case Choice.TurnOff:
                    game.Reminder = null;
                    return true;
                default:
                    return false;                   // Got it — the next one comes on schedule
            }
        }

        /// <summary>When the soonest active reminder is due (null if there are none).</summary>
        public static DateTime? NextWake(IEnumerable<Game> games) =>
            games.Where(g => g.HasReminder).Select(g => (DateTime?)g.Reminder!.NextAt).Min();

        // ---- Windows scheduled task: wakes Game Hunter for reminders while it's closed ----

        private static readonly object SyncGate = new();

        /// <summary>
        /// Point the scheduled task at the soonest reminder (or remove it when there are none).
        /// Runs schtasks off the UI thread; safe to call after every reminder change.
        /// </summary>
        public static void SyncWakeTask(IEnumerable<Game> games)
        {
            var next = NextWake(games);
            var exe = Environment.ProcessPath;
            Task.Run(() => SyncWakeTaskNow(next, exe));
        }

        /// <summary>Synchronous version (used at exit and in tests). Returns schtasks' exit code.</summary>
        public static int SyncWakeTaskNow(DateTime? next, string? exePath)
        {
            lock (SyncGate)
            {
                try
                {
                    if (next == null) return Schtasks($"/Delete /TN \"{TaskName}\" /F", out _);
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return -1;

                    // A time that's already passed: wake in a moment (the app/runner shows it).
                    var at = next.Value < DateTime.Now.AddSeconds(30) ? DateTime.Now.AddSeconds(30) : next.Value;
                    var xml = TaskXml(at, exePath);
                    var tmp = Path.Combine(Path.GetTempPath(), "gh-reminder-task.xml");
                    File.WriteAllText(tmp, xml, Encoding.Unicode);
                    try { return Schtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F", out _); }
                    finally { try { File.Delete(tmp); } catch { } }
                }
                catch { return -1; }
            }
        }

        /// <summary>The task's definition: one exact time, runs as the signed-in user (so the
        /// pop-up is visible), catches up if the PC was asleep/off then, no time limit.</summary>
        public static string TaskXml(DateTime at, string exePath) =>
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Shows your Game Hunter game reminders while the app is closed. Removed automatically when you have no reminders.</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>{at:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{System.Security.SecurityElement.Escape(exePath)}""</Command>
      <Arguments>{WakeArg}</Arguments>
    </Exec>
  </Actions>
</Task>";

        private static int Schtasks(string args, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return p.ExitCode;
        }
    }
}
