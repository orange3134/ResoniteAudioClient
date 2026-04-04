using System;
using FrooxEngine;

namespace AudioClient;

public class DummySystemInfo : ISystemInfo
{
    public Platform Platform => Environment.OSVersion.Platform == PlatformID.Win32NT ? Platform.Windows : Platform.Linux;

    public string UniqueDeviceIdentifier => "AudioClient-Headless-123456789";

    public bool IsAOT => false;

    public string OperatingSystem => Environment.OSVersion.VersionString;

    public string CPU => "Dummy AudioClient CPU";

    public string GPU => "Headless Render Engine";

    public int? PhysicalCores => Environment.ProcessorCount;

    public long MemoryBytes => 16L * 1024 * 1024 * 1024; // Dummy 16GB

    public long VRAMBytes => 0; // Headless

    public string XRDeviceName => "None";

    public string XRDeviceModel => "None";

    public void RegisterThread(string name)
    {
        // No-op for profiler
    }

    public void BeginSample(string name)
    {
        // No-op
    }

    public void EndSample()
    {
        // No-op
    }
}
