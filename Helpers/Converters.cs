using System;
using Microsoft.UI.Xaml;

namespace NextValleyDock.Helpers
{
    public static class Converters
    {
        public static Visibility IsRunningToVisibility(bool isRunning)
        {
            return isRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        public static double IsRunningToOpacity(bool isRunning)
        {
            return isRunning ? 1.0 : 0.6;
        }

        public static Visibility IsPinnedToVisibility(bool isPinned)
        {
            return isPinned ? Visibility.Visible : Visibility.Collapsed;
        }

        public static Visibility IsNotPinnedToVisibility(bool isPinned)
        {
            return isPinned ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
