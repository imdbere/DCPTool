using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace DCP_Tool
{
    /// <summary>
    /// Interaktionslogik für "App.xaml"
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var openFile = e.Args.Length > 0 ? e.Args[0] : null;

            var mainWindow = new MainWindow(openFile);
            mainWindow.Show();
        }
    }
}
