using FihLens.WinUI.Presentation;
using FihLens.Windows.Startup;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FihLens.WinUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppInstance? current = null;
        ActivationLaunchDecision decision;
        try
        {
            AppInstance currentInstance = AppInstance.GetCurrent();
            current = currentInstance;
            SingleInstanceActivation.Initialize(currentInstance);
            AppInstance registration = AppInstance.FindOrRegisterForKey(SingleInstanceActivation.InstanceKey);
            AppActivationArguments activationArgs = currentInstance.GetActivatedEventArgs();
            decision = SingleInstanceActivationPolicy.Decide(registration.IsCurrent, () =>
                SecondaryActivationWait.Redirect(() => registration.RedirectActivationToAsync(activationArgs).AsTask()) switch
                {
                    SecondaryActivationWaitResult.Completed => SecondaryActivationResult.Completed,
                    SecondaryActivationWaitResult.TimedOut => SecondaryActivationResult.TimedOut,
                    _ => SecondaryActivationResult.Failed
                });
            if (decision == ActivationLaunchDecision.Primary)
                SingleInstanceActivation.RetainRegistration(registration);
            else
                SingleInstanceActivation.Detach();
        }
        catch (Exception exception)
        {
            if (current is not null) SingleInstanceActivation.Detach();
            System.Diagnostics.Debug.WriteLine($"Activation startup exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
            SingleInstanceActivation.ShowStartupFailure(false);
            return;
        }

        if (SingleInstanceActivationPolicy.ShouldShowStartupFailure(decision))
        {
            SingleInstanceActivation.ShowStartupFailure(decision == ActivationLaunchDecision.TimedOut);
            return;
        }
        if (decision == ActivationLaunchDecision.Redirected) return;

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
