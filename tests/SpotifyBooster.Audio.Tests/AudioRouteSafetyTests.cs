using SpotifyBooster.Audio.Devices;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Audio.Tests;

public sealed class AudioRouteSafetyTests
{
    [Fact]
    public void VirtualCaptureToPhysicalOutput_IsAccepted()
    {
        AudioDeviceInfo source = Device(
            "capture",
            "Virtual Cable Output",
            AudioDeviceDirection.Capture,
            isVirtual: true);
        AudioDeviceInfo output = Device(
            "render",
            "USB Headphones",
            AudioDeviceDirection.Render,
            isVirtual: false);

        AudioRouteValidationResult result = AudioRouteSafety.Validate(source, output);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SameCableFamily_IsRejectedAsFeedbackRisk()
    {
        AudioDeviceInfo source = Device(
            "capture",
            "CABLE Output (VB-Audio Virtual Cable)",
            AudioDeviceDirection.Capture,
            isVirtual: true);
        AudioDeviceInfo output = Device(
            "render",
            "CABLE Input (VB-Audio Virtual Cable)",
            AudioDeviceDirection.Render,
            isVirtual: true);

        AudioRouteValidationResult result = AudioRouteSafety.Validate(source, output);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("boucle", StringComparison.Ordinal));
    }

    [Fact]
    public void MultichannelSource_IsRejected()
    {
        AudioDeviceInfo source = Device(
            "capture",
            "Eight channels",
            AudioDeviceDirection.Capture,
            isVirtual: true) with
        {
            Channels = 8,
        };
        AudioDeviceInfo output = Device(
            "render",
            "Speakers",
            AudioDeviceDirection.Render,
            isVirtual: false);

        AudioRouteValidationResult result = AudioRouteSafety.Validate(source, output);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonStereoOutput_IsRejected()
    {
        AudioDeviceInfo source = Device(
            "capture",
            "Virtual Cable Output",
            AudioDeviceDirection.Capture,
            isVirtual: true);
        AudioDeviceInfo output = Device(
            "render",
            "Surround Speakers",
            AudioDeviceDirection.Render,
            isVirtual: false) with
        {
            Channels = 6,
        };

        AudioRouteValidationResult result = AudioRouteSafety.Validate(source, output);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("sortie configurée en stéréo", StringComparison.Ordinal));
    }

    [Fact]
    public void PhysicalCapture_IsRejectedForMvp()
    {
        AudioRouteValidationResult result = AudioRouteSafety.Validate(
            Device(
                "capture",
                "USB Microphone",
                AudioDeviceDirection.Capture,
                isVirtual: false),
            Device(
                "render",
                "Speakers",
                AudioDeviceDirection.Render,
                isVirtual: false));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("capture virtuel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Generic Virtual Output")]
    [InlineData("Generic Loopback Output")]
    [InlineData("Generic Streaming Output")]
    public void VirtualOutputMarkers_AreRejectedForMvp(string outputName)
    {
        AudioRouteValidationResult result = AudioRouteSafety.Validate(
            Device(
                "capture",
                "Spotify Capture",
                AudioDeviceDirection.Capture,
                isVirtual: true),
            Device(
                "render",
                outputName,
                AudioDeviceDirection.Render,
                isVirtual: false));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("rendu physique", StringComparison.Ordinal));
    }

    private static AudioDeviceInfo Device(
        string id,
        string name,
        AudioDeviceDirection direction,
        bool isVirtual)
    {
        return new(
            id,
            name,
            direction,
            false,
            48_000,
            2,
            32,
            isVirtual);
    }
}
