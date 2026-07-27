using System.Windows.Input;

namespace SpotifyBooster.App.Mvvm;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool>? canExecute;
    private readonly Action<Exception> errorHandler;
    private int isExecuting;

    public AsyncRelayCommand(
        Func<Task> execute,
        Action<Exception> errorHandler,
        Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.errorHandler = errorHandler;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return Volatile.Read(ref isExecuting) == 0
            && (canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)
            || Interlocked.Exchange(ref isExecuting, 1) != 0)
        {
            return;
        }

        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            errorHandler(exception);
        }
        finally
        {
            Volatile.Write(ref isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
