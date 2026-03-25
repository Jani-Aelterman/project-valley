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

        protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, Microsoft.UI.Xaml.XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
            _controller = new Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController();
            
            var configuration = new Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration();
            
            // CRUCIAL: Force it to be always active so it never dims when clicking outside the panel
            configuration.IsInputActive = true; 

            switch (Application.Current.RequestedTheme)
            {
                case ApplicationTheme.Dark: configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark; break;
                case ApplicationTheme.Light: configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light; break;
                default: configuration.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Default; break;
            }

            _controller.SetSystemBackdropConfiguration(configuration);
            _controller.AddSystemBackdropTarget(connectedTarget);
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
