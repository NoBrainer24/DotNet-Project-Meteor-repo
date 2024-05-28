using System;
using System.IO;
using DotNet.Meteor.Debug.Extensions;
using DotNet.Meteor.Debug.Sdk;
using DotNet.Meteor.Processes;
using DotNet.Meteor.Shared;
using Mono.Debugging.Soft;

namespace DotNet.Meteor.Debug;

public class NoDebugLaunchAgent : BaseLaunchAgent {
    public NoDebugLaunchAgent(LaunchConfiguration configuration) : base(configuration) { }
    public override void Launch(IProcessLogger logger) {
        if (Configuration.Device.IsAndroid)
            LaunchAndroid(logger);
        if (Configuration.Device.IsIPhone)
            LaunchAppleMobile(logger);
        if (Configuration.Device.IsMacCatalyst)
            LaunchMacCatalyst(logger);
        if (Configuration.Device.IsWindows)
            LaunchWindows(logger);
    }
    public override void Connect(SoftDebuggerSession session) {
        ConnectHotReload();
    }

    private void LaunchAppleMobile(IProcessLogger logger) {
        if (Configuration.Device.IsEmulator) {
            var appProcess = MonoLaunch.DebugSim(Configuration.Device.Serial, Configuration.OutputAssembly, Configuration.DebugPort, logger);
            Disposables.Add(() => appProcess.Terminate());
        } else {
            var hotReloadPortForwarding = MonoLaunch.TcpTunnel(Configuration.Device.Serial, Configuration.ReloadHostPort, logger);
            MonoLaunch.InstallDev(Configuration.Device.Serial, Configuration.OutputAssembly, logger);
            var appProcess = MonoLaunch.DebugDev(Configuration.Device.Serial, Configuration.OutputAssembly, Configuration.DebugPort, logger);
            Disposables.Add(() => appProcess.Terminate());
            Disposables.Add(() => hotReloadPortForwarding.Terminate());
        }
    }
    private void LaunchMacCatalyst(IProcessLogger logger) {
        var tool = AppleSdk.OpenTool();
        var processRunner = new ProcessRunner(tool, new ProcessArgumentBuilder().AppendQuoted(Configuration.OutputAssembly));
        var result = processRunner.WaitForExit();

        if (!result.Success)
            throw ServerExtensions.GetProtocolException(string.Join(Environment.NewLine, result.StandardError));
    }
    private void LaunchWindows(IProcessLogger logger) {
        var program = new FileInfo(Configuration.OutputAssembly);
        var process = new ProcessRunner(program, new ProcessArgumentBuilder(), logger).Start();
        Disposables.Add(() => process.Terminate());
    }
    private void LaunchAndroid(IProcessLogger logger) {
        var applicationId = Configuration.GetApplicationName();
        if (Configuration.Device.IsEmulator)
            Configuration.Device.Serial = AndroidEmulator.Run(Configuration.Device.Name).Serial;

        DeviceBridge.Forward(Configuration.Device.Serial, Configuration.ReloadHostPort);
        Disposables.Add(() => DeviceBridge.RemoveForward(Configuration.Device.Serial));

        if (Configuration.UninstallApp)
            DeviceBridge.Uninstall(Configuration.Device.Serial, applicationId, logger);

        DeviceBridge.Install(Configuration.Device.Serial, Configuration.OutputAssembly, logger);
        DeviceBridge.Launch(Configuration.Device.Serial, applicationId, logger);
        DeviceBridge.Flush(Configuration.Device.Serial);

        var logcatProcess = DeviceBridge.Logcat(Configuration.Device.Serial, logger);
        Disposables.Add(() => logcatProcess.Terminate());
    }
}