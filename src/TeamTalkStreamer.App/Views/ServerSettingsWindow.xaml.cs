#nullable enable

#region Usings
using System.Windows;
using System.Windows.Controls;
using TeamTalkStreamer.App.ViewModels;
#endregion

namespace TeamTalkStreamer.App.Views;

#region Class: ServerSettingsWindow
/// <summary>
/// Code-behind for the server-settings dialog. Two responsibilities:
/// <list type="bullet">
///   <item><description>Wire the VM to <see cref="FrameworkElement.DataContext"/>.</description></item>
///   <item><description>Sync each <see cref="PasswordBox"/> with the VM,
///     since <c>PasswordBox.Password</c> is not bindable in WPF.</description></item>
/// </list>
/// </summary>
public partial class ServerSettingsWindow : Window
{
    #region Fields
    // Held by reference so the PasswordChanged handlers can push values
    // back into it without a DataContext cast on every keystroke.
    private readonly ServerSettingsViewModel _viewModel;
    #endregion

    #region Constructor

    /// <param name="viewModel">Injected by DI; already populated with
    /// the current settings.</param>
    public ServerSettingsWindow(ServerSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // VM asks to close via this event; honor it and carry the
        // DialogResult so callers of ShowDialog() get a useful bool.
        _viewModel.CloseRequested += OnCloseRequested;
    }

    #endregion

    #region Window lifecycle

    /// <summary>Once XAML is built, push the VM's initial password
    /// values into the three <see cref="PasswordBox"/>es. The reverse
    /// direction (user typing -> VM) is handled by the
    /// PasswordChanged handlers below.</summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ServerPasswordBox.Password = _viewModel.ServerPassword;
        UserPasswordBox.Password = _viewModel.Password;
        ChannelPasswordBox.Password = _viewModel.ChannelPassword;
    }

    /// <summary>Translate <see cref="ServerSettingsViewModel.CloseRequested"/>
    /// into an actual <see cref="Window.Close"/> with the matching
    /// <see cref="Window.DialogResult"/>.</summary>
    private void OnCloseRequested(object? sender, bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    #endregion

    #region PasswordBox sync
    // One handler per PasswordBox. Pushes the new password into the
    // corresponding VM property on every keystroke so a subsequent
    // Save picks it up.

    private void ServerPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) _viewModel.ServerPassword = pb.Password;
    }

    private void UserPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) _viewModel.Password = pb.Password;
    }

    private void ChannelPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) _viewModel.ChannelPassword = pb.Password;
    }

    #endregion
}
#endregion
