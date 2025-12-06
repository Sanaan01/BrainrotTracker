using System;
using Microsoft.UI.Dispatching;

namespace Brainrot.UI
{
    // Simplified manager inspired by Awqat; uses existing TrayIconHost and TaskbarWidget.
    internal static class TaskBarManager
    {
        private static TaskbarWidget? _widget;
        private static DispatcherQueue? _dispatcher;
        private static int _offset = -1;

        public static void Initialize(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            if (_widget == null)
            {
                _widget = new TaskbarWidget();
            }
        }

        public static void UpdateContent(string title, string emoji, string focus, string neutral, string rot, double focusPct)
        {
            _widget?.UpdateSnapshot(title, emoji, focus, neutral, rot, focusPct);
        }

        public static void ShowWidget(bool forceReset)
        {
            if (forceReset)
                _offset = -1;
            _widget?.MoveToTaskbarSlot(_offset);
            _widget?.ShowWidget();
        }

        public static void HideWidget()
        {
            _widget?.HideWidget();
        }
    }
}
