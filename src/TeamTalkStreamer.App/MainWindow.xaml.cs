#region Usings
using System.Windows;
using TeamTalkStreamer.App.ViewModels;
#endregion

namespace TeamTalkStreamer.App;

#region Class: MainWindow
/// <summary>
/// Code-behind for the main window. Intentionally almost empty — all
/// UI logic lives in <see cref="MainViewModel"/>; this class just
/// wires the view model to the window's DataContext and initializes
/// the generated XAML.
/// </summary>
public partial class MainWindow : Window
{
    #region Constructor

    /// <param name="viewModel">Injected by the DI container; exposes
    /// every property the XAML binds to.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    #endregion
}
#endregion
