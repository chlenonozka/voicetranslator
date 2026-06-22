using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using VoiceTranslator.App.ViewModels;

namespace VoiceTranslator.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? subscribedViewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as MainViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(null);
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            SubscribeToViewModel(e.NewValue as MainViewModel);
        }
    }

    private void SubscribeToViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(subscribedViewModel, viewModel))
        {
            return;
        }

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        subscribedViewModel = viewModel;

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.StatusMessage)
            || sender is not MainViewModel sourceViewModel)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () => RaiseStatusLiveRegionChanged(sourceViewModel));
    }

    private void RaiseStatusLiveRegionChanged(MainViewModel sourceViewModel)
    {
        if (!IsLoaded
            || !ReferenceEquals(subscribedViewModel, sourceViewModel)
            || !ReferenceEquals(DataContext, sourceViewModel))
        {
            return;
        }

        AutomationPeer? peer =
            UIElementAutomationPeer.FromElement(StatusTextBlock)
            ?? UIElementAutomationPeer.CreatePeerForElement(StatusTextBlock);

        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
