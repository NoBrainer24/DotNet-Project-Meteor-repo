using DotNet.Meteor.Debug.Extensions;
using DotNet.Meteor.Common;
using Mono.Debugging.Soft;
using DotNet.Meteor.Common.Processes;
using DotNet.Meteor.Common.Apple;
using DotNet.Meteor.Common.Android;

namespace DotNet.Meteor.Debug;

public class NoDebugLaunchAgent : BaseLaunchAgent {
    public NoDebugLaunchAgent(LaunchConfiguration configuration) : base(configuration) { }
    public override void Launch(DebugSession debugSession) {
        if (Configuration.Device.IsAndroid)
            LaunchAndroid(debugSession);
        if (Configuration.Device.IsIPhone)
            LaunchAppleMobile(debugSession);
        if (Configuration.Device.IsMacCatalyst)
            LaunchMacCatalyst(debugSession);
        if (Configuration.Device.IsWindows)
            LaunchWindows(debugSession);
    }
    public override void Connect(SoftDebuggerSession session) {}

    private void LaunchAppleMobile(DebugSession debugSession) {
        if (RuntimeSystem.IsMacOS) {
            if (Configuration.Device.IsEmulator) {
                var appProcess = MonoLauncher.DebugSim(Configuration.Device.Serial, Configuration.ProgramPath, Configuration.DebugPort, debugSession);
                Disposables.Add(() => appProcess.Terminate());
            } else {
                var hotReloadPortForwarding = MonoLauncher.TcpTunnel(Configuration.Device.Serial, Configuration.ReloadHostPort, debugSession);
                Disposables.Add(() => hotReloadPortForwarding.Terminate());
                MonoLauncher.InstallDev(Configuration.Device.Serial, Configuration.ProgramPath, debugSession);
                var appProcess = MonoLauncher.DebugDev(Configuration.Device.Serial, Configuration.ProgramPath, Configuration.DebugPort, debugSession);
                Disposables.Add(() => appProcess.Terminate());
            }
        } else {
            var forwardingProcess = IDeviceTool.Proxy(Configuration.Device.Serial, Configuration.ReloadHostPort, debugSession);
            Disposables.Add(() => forwardingProcess.Terminate());
            IDeviceTool.Installer(Configuration.Device.Serial, Configuration.ProgramPath, debugSession);
            debugSession.OnImportantDataReceived("Application installed on device. Please tap on the app icon to run it.");
        }
    }
    private void LaunchMacCatalyst(IProcessLogger logger) {
        var tool = AppleSdkLocator.OpenTool();
        var processRunner = new ProcessRunner(tool, new ProcessArgumentBuilder().AppendQuoted(Configuration.ProgramPath));
        var result = processRunner.WaitForExit();

        if (!result.Success)
            throw ServerExtensions.GetProtocolException(string.Join(Environment.NewLine, result.StandardError));
    }
    private void LaunchWindows(IProcessLogger logger) {
        var program = new FileInfo(Configuration.ProgramPath);
        var process = new ProcessRunner(program, new ProcessArgumentBuilder(), logger).Start();
        Disposables.Add(() => process.Terminate());
    }
    private void LaunchAndroid(IProcessLogger logger) {
        var applicationId = Configuration.GetApplicationName();
        if (Configuration.Device.IsEmulator)
            Configuration.Device.Serial = AndroidEmulator.Run(Configuration.Device.Name).Serial;

        AndroidDebugBridge.Forward(Configuration.Device.Serial, Configuration.ReloadHostPort);
        Disposables.Add(() => AndroidDebugBridge.RemoveForward(Configuration.Device.Serial));

        if (Configuration.UninstallApp)
            AndroidDebugBridge.Uninstall(Configuration.Device.Serial, applicationId, logger);

        AndroidDebugBridge.Install(Configuration.Device.Serial, Configuration.ProgramPath, logger);

        AndroidFastDev.TryPushAssemblies(Configuration.Device, Configuration.AssetsPath, applicationId, logger);

        AndroidDebugBridge.Launch(Configuration.Device.Serial, applicationId, logger);
        AndroidDebugBridge.Flush(Configuration.Device.Serial);

        var logcatProcess = AndroidDebugBridge.Logcat(Configuration.Device.Serial, logger);
        Disposables.Add(() => logcatProcess.Terminate());
    }
}