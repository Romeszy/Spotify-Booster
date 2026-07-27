using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Services;

public sealed class ApplicationState
{
    private readonly object syncRoot = new();
    private AppSettings settings = new();
    private IReadOnlyList<AudioDeviceInfo> captureDevices = [];
    private IReadOnlyList<AudioDeviceInfo> renderDevices = [];
    private IReadOnlyList<SpotifyProcessInfo> spotifyProcesses = [];
    private AudioEngineStatus engineStatus = AudioEngineStatus.Stopped;

    public event EventHandler? Changed;

    public AppSettings Settings
    {
        get
        {
            lock (syncRoot)
            {
                return settings;
            }
        }
    }

    public IReadOnlyList<AudioDeviceInfo> CaptureDevices
    {
        get
        {
            lock (syncRoot)
            {
                return captureDevices;
            }
        }
    }

    public IReadOnlyList<AudioDeviceInfo> RenderDevices
    {
        get
        {
            lock (syncRoot)
            {
                return renderDevices;
            }
        }
    }

    public IReadOnlyList<SpotifyProcessInfo> SpotifyProcesses
    {
        get
        {
            lock (syncRoot)
            {
                return spotifyProcesses;
            }
        }
    }

    public AudioEngineStatus EngineStatus
    {
        get
        {
            lock (syncRoot)
            {
                return engineStatus;
            }
        }
    }

    public void SetSettings(AppSettings value)
    {
        lock (syncRoot)
        {
            settings = value;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDevices(
        IReadOnlyList<AudioDeviceInfo> capture,
        IReadOnlyList<AudioDeviceInfo> render)
    {
        lock (syncRoot)
        {
            captureDevices = capture;
            renderDevices = render;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSpotifyProcesses(IReadOnlyList<SpotifyProcessInfo> processes)
    {
        lock (syncRoot)
        {
            spotifyProcesses = processes;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetEngineStatus(AudioEngineStatus status)
    {
        lock (syncRoot)
        {
            engineStatus = status;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
