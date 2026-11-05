using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SignMyApp.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Raise for every property: cheap for these small view models, and nothing goes stale.</summary>
    protected void OnAllChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    readonly Action<object?> _run;
    readonly Func<object?, bool>? _can;

    public RelayCommand(Action run, Func<bool>? can = null)
        : this(_ => run(), can is null ? null : _ => can()) { }

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) _run(parameter); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>An async command. Execute returns at the first await; tests await <see cref="RunAsync"/>.</summary>
public sealed class AsyncCommand : ICommand
{
    readonly Func<Task> _run;
    readonly Func<bool>? _can;
    bool _busy;

    public AsyncCommand(Func<Task> run, Func<bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy && (_can?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        try { await RunAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    public async Task RunAsync()
    {
        if (!CanExecute(null)) return;
        _busy = true;
        Refresh();
        try { await _run(); }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
