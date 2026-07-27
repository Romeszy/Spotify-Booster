using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using SpotifyBooster.App.Mvvm;
using SpotifyBooster.App.ViewModels;
using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly ILogger<GlobalHotkeyService> logger;
    private readonly Dictionary<int, Action> actions = [];
    private readonly List<string> registrationErrors = [];
    private HwndSource? source;
    private MainWindow? window;
    private MainViewModel? viewModel;
    private HotkeySettings? registeredSettings;
    private int nextId = 0x5342;
    private int disposed;

    public event EventHandler<HotkeyRegistrationFailedEventArgs>?
        RegistrationFailed;

    public IReadOnlyList<string> RegistrationErrors => registrationErrors;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        this.logger = logger;
    }

    public void Initialize(
        MainWindow mainWindow,
        MainViewModel mainViewModel)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(mainViewModel);

        Detach();
        window = mainWindow;
        viewModel = mainViewModel;

        nint handle = new WindowInteropHelper(mainWindow).EnsureHandle();
        source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "Impossible d’attacher les raccourcis à la fenêtre principale.");
        source.AddHook(WindowProcedure);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RegisterConfiguredHotkeys();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Detach();
    }

    private void RegisterConfiguredHotkeys()
    {
        MainViewModel? currentViewModel = viewModel;
        if (currentViewModel is null)
        {
            return;
        }

        HotkeySettings settings = currentViewModel.CurrentSettings.Hotkeys;
        if (settings == registeredSettings)
        {
            return;
        }

        registrationErrors.Clear();
        UnregisterAll();
        registeredSettings = settings;
        if (!settings.GlobalHotkeysEnabled)
        {
            return;
        }

        Register(
            nameof(settings.IncreaseGain),
            settings.IncreaseGain,
            () => currentViewModel.AdjustGainDb(1));
        Register(
            nameof(settings.DecreaseGain),
            settings.DecreaseGain,
            () => currentViewModel.AdjustGainDb(-1));
        Register(
            nameof(settings.ToggleProcessing),
            settings.ToggleProcessing,
            () => Execute(currentViewModel.ToggleEngineCommand));
        Register(
            nameof(settings.PanicReset),
            settings.PanicReset,
            currentViewModel.PanicReset);
        Register(
            nameof(settings.ToggleBassBoost),
            settings.ToggleBassBoost,
            currentViewModel.ToggleBassBoost);
        Register(
            nameof(settings.ToggleWindow),
            settings.ToggleWindow,
            ToggleWindow);
    }

    private void Register(
        string settingName,
        string value,
        Action action)
    {
        if (!TryParse(value, out HotkeyGesture gesture, out string error))
        {
            ReportFailure(settingName, value, error, null);
            return;
        }

        int id = nextId++;
        nint handle = source?.Handle ?? 0;
        if (!RegisterHotKey(
                handle,
                id,
                gesture.Modifiers | ModNoRepeat,
                gesture.VirtualKey))
        {
            int nativeError = Marshal.GetLastWin32Error();
            ReportFailure(
                settingName,
                value,
                "Le raccourci est déjà utilisé ou réservé par Windows.",
                nativeError);
            return;
        }

        actions.Add(id, action);
    }

    private void ReportFailure(
        string settingName,
        string value,
        string message,
        int? nativeError)
    {
        string description = $"{settingName} ({value}) : {message}";
        registrationErrors.Add(description);
        RegistrationFailed?.Invoke(
            this,
            new HotkeyRegistrationFailedEventArgs(
                settingName,
                value,
                message,
                nativeError));
    }

    private static bool TryParse(
        string value,
        out HotkeyGesture gesture,
        out string error)
    {
        gesture = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "La combinaison est vide.";
            return false;
        }

        string[] parts = value.Split('+');
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            error = "Utilisez au moins un modificateur et une touche, séparés par +.";
            return false;
        }

        uint modifiers = 0;
        string? keyToken = null;
        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();
            uint modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0,
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"Le modificateur « {part} » est répété.";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (keyToken is not null)
            {
                error = "Une combinaison doit contenir exactement une touche.";
                return false;
            }

            keyToken = part;
        }

        if (modifiers == 0 || keyToken is null)
        {
            error = "La combinaison doit contenir un modificateur et une touche.";
            return false;
        }

        string normalizedKey = keyToken.Length == 1
            && char.IsDigit(keyToken[0])
                ? $"D{keyToken}"
                : keyToken;
        if ((keyToken.Length > 1 && keyToken.All(char.IsDigit))
            || !Enum.TryParse(normalizedKey, true, out Key key)
            || !Enum.IsDefined(key)
            || key is Key.None
                or Key.LeftAlt
                or Key.RightAlt
                or Key.LeftCtrl
                or Key.RightCtrl
                or Key.LeftShift
                or Key.RightShift
                or Key.LWin
                or Key.RWin)
        {
            error = $"La touche « {keyToken} » n’est pas reconnue.";
            return false;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            error = $"La touche « {keyToken} » ne peut pas être globale.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, (uint)virtualKey);
        return true;
    }

    private static void Execute(AsyncRelayCommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void ToggleWindow()
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        if (currentWindow.IsVisible
            && currentWindow.WindowState != WindowState.Minimized)
        {
            currentWindow.Hide();
            return;
        }

        currentWindow.RestoreFromTray();
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmHotkey
            && actions.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            handled = true;
            action();
        }

        return 0;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.CurrentSettings)
            or nameof(MainViewModel.GlobalHotkeysEnabled))
        {
            RegisterConfiguredHotkeys();
        }
    }

    private void UnregisterAll()
    {
        nint handle = source?.Handle ?? 0;
        foreach (int id in actions.Keys)
        {
            if (!UnregisterHotKey(handle, id))
            {
                int nativeError = Marshal.GetLastWin32Error();
                string message =
                    $"Désinscription du raccourci natif {id} impossible (Win32 {nativeError}).";
                registrationErrors.Add(message);
                logger.LogWarning(
                    "La désinscription du raccourci global {HotkeyId} a échoué avec Win32 {NativeError}.",
                    id,
                    nativeError);
            }
        }

        actions.Clear();
        nextId = 0x5342;
    }

    private void Detach()
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        UnregisterAll();
        registeredSettings = null;
        source?.RemoveHook(WindowProcedure);
        source = null;
        window = null;
        viewModel = null;
        registeredSettings = null;
        registrationErrors.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        nint hwnd,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    private readonly record struct HotkeyGesture(
        uint Modifiers,
        uint VirtualKey);
}

public sealed class HotkeyRegistrationFailedEventArgs : EventArgs
{
    public HotkeyRegistrationFailedEventArgs(
        string settingName,
        string gesture,
        string message,
        int? nativeErrorCode)
    {
        SettingName = settingName;
        Gesture = gesture;
        Message = message;
        NativeErrorCode = nativeErrorCode;
    }

    public string SettingName { get; }

    public string Gesture { get; }

    public string Message { get; }

    public int? NativeErrorCode { get; }
}
