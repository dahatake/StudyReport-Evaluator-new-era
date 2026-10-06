using Avalonia;
using Avalonia.Headless.XUnit;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Composition;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void Registration_scopes_navigator_as_singleton_and_view_models_as_transient()
    {
        WorkflowNavigator navigator = new();
        ServiceRegistration services = new(navigator);
        MainWindowViewModel first = services.CreateMainWindowViewModel();
        MainWindowViewModel second = services.CreateMainWindowViewModel();

        try
        {
            Assert.Same(navigator, services.WorkflowNavigator);
            Assert.NotSame(first, second);
            Assert.Same(navigator, first.Navigator);
            Assert.Same(navigator, second.Navigator);

            Assert.True(first.NextCommand.CanExecute(null));
            first.NextCommand.Execute(null);
            Assert.Equal(WorkflowStep.Design, second.CurrentStep);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public void Registration_rejects_a_missing_singleton_dependency()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceRegistration(null!));
    }

    [AvaloniaFact]
    public void Window_factory_is_transient_and_app_factory_uses_its_registered_navigator()
    {
        ServiceRegistration services = new();
        MainWindow first = services.CreateMainWindow();
        MainWindow second = services.CreateMainWindow();

        try
        {
            Assert.NotSame(first, second);
            Assert.NotSame(first.ViewModel, second.ViewModel);
            Assert.Same(services.WorkflowNavigator, first.ViewModel.Navigator);
            Assert.Same(services.WorkflowNavigator, second.ViewModel.Navigator);
            Assert.Same(first.ViewModel, first.DataContext);
            Assert.Equal(WorkflowStep.Input, first.ViewModel.CurrentStep);

            App app = Assert.IsType<App>(Application.Current);
            MainWindow appWindow = app.CreateMainWindow();
            try
            {
                Assert.Same(app.Services.WorkflowNavigator, appWindow.ViewModel.Navigator);
                Assert.Same(appWindow.ViewModel, appWindow.DataContext);
                Assert.Equal(WorkflowStep.Input, appWindow.ViewModel.CurrentStep);
            }
            finally
            {
                appWindow.ViewModel.Dispose();
            }
        }
        finally
        {
            first.ViewModel.Dispose();
            second.ViewModel.Dispose();
        }
    }
}