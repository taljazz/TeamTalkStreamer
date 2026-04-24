#region Usings
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.App.ViewModels;

#region Class: MainViewModel (partial — source list)
/// <summary>
/// Source-list management: the observable collection bound to the UI
/// and the add / remove commands behind the toolbar buttons.
/// </summary>
public sealed partial class MainViewModel
{
    #region Bindable collection

    /// <summary>Live list of sources currently attached to the router.
    /// Bound to the ListBox in <c>MainWindow.xaml</c>.</summary>
    public ObservableCollection<IAudioSource> Sources { get; }

    #endregion

    #region Commands

    private RelayCommand? _addLoopbackSourceCommand;
    public ICommand AddLoopbackSourceCommand =>
        _addLoopbackSourceCommand ??= new RelayCommand(_ => AddLoopbackSource());

    private RelayCommand? _removeSelectedSourceCommand;
    public ICommand RemoveSelectedSourceCommand =>
        _removeSelectedSourceCommand ??= new RelayCommand(
            p => RemoveSource(p as IAudioSource),
            p => p is IAudioSource);

    #endregion

    #region Command handlers

    private void AddLoopbackSource()
    {
        if (Sources.Contains(_loopbackSource)) return;

        _router.AttachSource(_loopbackSource);
        Sources.Add(_loopbackSource);
        _speech.Speak($"{_loopbackSource.DisplayName} added.");
    }

    private void RemoveSource(IAudioSource? source)
    {
        if (source is null) return;
        _router.DetachSource(source.Id);
        Sources.Remove(source);
        _speech.Speak($"{source.DisplayName} removed.");
    }

    #endregion
}
#endregion

#region Class: RelayCommand
/// <summary>
/// Tiny <see cref="ICommand"/> implementation used by the view model.
/// Lives here rather than in its own file because it's only a handful
/// of lines and only the view model uses it. If other view models show
/// up, promote it to its own file.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    #region Fields
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    #endregion

    #region Constructor
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }
    #endregion

    #region ICommand
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    #endregion
}
#endregion
