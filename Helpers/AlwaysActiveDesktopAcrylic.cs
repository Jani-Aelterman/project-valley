using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;

namespace NextValleyDock.Helpers
{
    // Custom SystemBackdrop that forces IsInputActive to true, preventing the backdrop from 
    // falling back to a solid color when the panel loses focus.
    public class AlwaysActiveDesktopAcrylic : Microsoft.UI.Xaml.Media.SystemBackdrop
    {
        private Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController? _controller;
        private Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration? _configuration;

        protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, Microsoft.UI.Xaml.XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
            _controller = new Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController();
            
            _configuration = new Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration();
            
            // CRUCIAL: Force it to be always active so it never dims when clicking outside the panel
            _configuration.IsInputActive = true; 

            UpdateTheme();

            _controller.SetSystemBackdropConfiguration(_configuration);
            _controller.AddSystemBackdropTarget(connectedTarget);
        }

        public void UpdateTheme()
        {
            if (_configuration == null) return;
            var theme = NextValleyDock.Helpers.SettingsManager.GetResolvedTheme();
            switch (theme)
            {
                case ElementTheme.Dark: _configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark; break;
                case ElementTheme.Light: _configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light; break;
                default: _configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Default; break;
            }
        }

        protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            base.OnTargetDisconnected(disconnectedTarget);
            if (_controller != null)
            {
                _controller.RemoveSystemBackdropTarget(disconnectedTarget);
                _controller.Dispose();
                _controller = null;
            }
        }
    }
}
