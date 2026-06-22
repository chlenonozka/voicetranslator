using System.ComponentModel;
using System.Reflection;
using System.Windows.Threading;
using FluentAssertions;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.App.Views;

namespace VoiceTranslator.UnitTests.ViewModels;

public sealed class MainWindowLifecycleTests
{
    [Fact]
    public void ChangingDataContextAfterUnloadDoesNotRestoreViewModelSubscription()
    {
        RunSta(() =>
        {
            MainViewModel initialViewModel = new();
            MainViewModel replacementViewModel = new();
            MainWindow window = new(initialViewModel);

            window.Show();
            GetWindowSubscriberCount(initialViewModel).Should().Be(1);

            window.Close();
            DrainDispatcher();
            GetWindowSubscriberCount(initialViewModel).Should().Be(0);

            window.DataContext = replacementViewModel;

            GetWindowSubscriberCount(replacementViewModel).Should().Be(0);
        });
    }

    private static void DrainDispatcher()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static int GetWindowSubscriberCount(
        MainViewModel viewModel)
    {
        FieldInfo field = typeof(MainViewModel).GetField(
            nameof(INotifyPropertyChanged.PropertyChanged),
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "PropertyChanged backing field was not found.");

        return (field.GetValue(viewModel) as MulticastDelegate)?
            .GetInvocationList()
            .Count(handler =>
                handler.Method.Name == "OnViewModelPropertyChanged") ?? 0;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new TargetInvocationException(failure);
        }
    }
}
