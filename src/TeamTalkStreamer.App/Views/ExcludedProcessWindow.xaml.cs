#nullable enable

#region Usings
using System.Windows;
using TeamTalkStreamer.App.ViewModels;
#endregion

namespace TeamTalkStreamer.App.Views;

#region Class: ExcludedProcessWindow
/// <summary>
/// Code-behind for the excluded-apps picker dialog. Symmetric with
/// <c>ServerSettingsWindow</c>: wires the VM to DataContext and
/// translates <see cref="ExcludedProcessViewModel.CloseRequested"/>
/// into an actual <see cref="Window.Close"/> with the matching
/// <see cref="Window.DialogResult"/>.
/// </summary>
public partial class ExcludedProcessWindow : Window
{
    #region Fields
    private readonly ExcludedProcessViewModel _viewModel;
    #endregion

    #region Constructor

    /// <param name="viewModel">Injected by DI; already populated from
    /// settings and pre-selects the current exclusion if live.</param>
    public ExcludedProcessWindow(ExcludedProcessViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
    }

    #endregion

    #region Close-requested translation

    private void OnCloseRequested(object? sender, bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    #endregion
}
#endregion
