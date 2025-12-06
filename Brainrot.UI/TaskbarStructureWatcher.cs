using System;

namespace Brainrot.UI
{
    internal enum TaskbarChangeReason
    {
        None,
        Alignment,
        TabletMode
    }

    internal class TaskbarChangedEventArgs : EventArgs
    {
        public TaskbarChangeReason Reason { get; }
        public bool IsTaskbarCentered { get; }
        public bool IsTaskbarWidgetsEnabled { get; }
        public bool IsTaskbarHidden { get; }

        public TaskbarChangedEventArgs(TaskbarChangeReason reason, bool centered = false, bool widgets = true, bool hidden = false)
        {
            Reason = reason;
            IsTaskbarCentered = centered;
            IsTaskbarWidgetsEnabled = widgets;
            IsTaskbarHidden = hidden;
        }
    }

    // Minimal watcher placeholder to satisfy Awqat-like API.
    internal sealed class TaskbarStructureWatcher : IDisposable
    {
        public event EventHandler<TaskbarChangedEventArgs>? TaskbarChangedNotificationStarted;
        public event EventHandler<TaskbarChangedEventArgs>? TaskbarChangedNotificationCompleted;

        public TaskbarStructureWatcher(IntPtr hwndTaskbar, IntPtr hwndReBar)
        {
        }

        public void Trigger(TaskbarChangeReason reason = TaskbarChangeReason.None)
        {
            var args = new TaskbarChangedEventArgs(reason);
            TaskbarChangedNotificationStarted?.Invoke(this, args);
            TaskbarChangedNotificationCompleted?.Invoke(this, args);
        }

        public void Dispose()
        {
        }
    }
}
