using System;
using System.Windows;

namespace DCP_Tool
{
    /// <summary>
    /// Interaktionslogik für BrowserWindow.xaml
    /// </summary>
    public partial class BrowserWindow : Window
    {
        public BrowserWindow(string url)
        {
            InitializeComponent();
            browser.Navigate(new Uri(url));
        }

    }
}
