using DCP_Tool.Properties;
using Microsoft.Win32;
using RazorEngine;
using RazorEngine.Templating; // For extension methods.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DCP_Tool
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<DCP> DCPs = new List<DCP>();
        DCPInterface DCPInterface;

        public MainWindow(string file)
        {
            InitializeComponent();

            this.Title = "DCP Tool " + System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
            dataGridDCP.ItemsSource = DCPs;
            dataGridDCP.AutoGeneratingColumn += (sender, e) =>
            {
                if (e.PropertyType == typeof(DateTime))
                {
                    ((DataGridTextColumn)e.Column).Binding = new Binding(e.PropertyName) { StringFormat = "d" };
                }
            };

            dataGridLine.AutoGeneratingColumn += (sender, e) =>
            {
                if (e.PropertyType == typeof(TimeSpan))
                {
                    //((DataGridTextColumn)e.Column).Binding = new Binding(e.PropertyName) { StringFormat = "mm\\:ss" };
                }
            };

            DCPInterface = new DCPInterface(Settings.Default.Username, Settings.Default.Password);

            if (file != null)
            {
                LoadDCPFile(File.OpenRead(file));
            }

            if (string.IsNullOrEmpty(Settings.Default.Username) || string.IsNullOrEmpty(Settings.Default.Password))
            {
                ShowLogin();
            }
        }

        async Task<DCPLine> ReadDCPLineFromFile(string filename)
        {
            var fileStream = File.OpenRead(filename);

            var tagFile = TagLib.File.Create(new TagLib.StreamFileAbstraction(filename, fileStream, fileStream));
            var tags = tagFile.GetTag(TagLib.TagTypes.Id3v2);
            using var sonofindInterface = new SonofindInterface();

            var cdTitle = (new FileInfo(filename)).Name.Split('_')[0];
            var sonofindRes = await sonofindInterface.QueryTitle(cdTitle);

            var line = new DCPLine()
            {
                AutoriString = tags.FirstComposer,
                Marca = sonofindRes != null ? "Sonoton" : "",
                Esecutori = sonofindRes?.artists ?? tags.Performers.Aggregate((s1, s2) => s1 + ", " + s2),
                Titolo = sonofindRes?.title ?? tags.Title,
                Durata = tagFile.Properties.Duration,
                SiglaNum = sonofindRes != null ? tags.Album.Split('-')[0] : tags.Album,
                TipoGenerazione = TipoGenerazione.OperaSuDisco,
                Ruolo = Ruolo.SF,
                Gensiae = GenereSIAE.ML
            };

            return line;
        }

        async void OpenFiles(string[] files)
        {
            progressBar.IsIndeterminate = true;
            var dcpList = await Task.WhenAll(files.Select(f => ReadDCPLineFromFile(f)));

            if (dataGridDCP.SelectedItem is DCP dcp)
            {
                dcp.Lines.AddRange(dcpList);
                dataGridLine.Items.Refresh();
            }

            progressBar.Value = 100;
            progressBar.IsIndeterminate = false;

        }

        private void dataGridLine_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                OpenFiles(files);
            }
        }

        private void menuItemSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog();
            dialog.Filter = "DCPs | *.dcp";
            dialog.DefaultExt = "dcp";

            if (dialog.ShowDialog() ?? false)
            {
                using var fileStream = dialog.OpenFile();

                var serializer = new DataContractJsonSerializer(typeof(DCP));
                dataGridLine.CommitEdit();
                dataGridDCP.CommitEdit();
                if (dataGridDCP.SelectedItem is DCP dcp)
                {
                    serializer.WriteObject(fileStream, dcp);
                }
                else
                {
                    MessageBox.Show("Please select a DCP first");
                }

            }
        }

        private void menuItemOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "DCPs | *.dcp";
            //dialog.Multiselect = true;
            //dialog.Filter = "*.mp3";

            if (dialog.ShowDialog() ?? false)
            {
                var fileStream = dialog.OpenFile();
                LoadDCPFile(fileStream);
            }
        }

        void LoadDCPFile(Stream fileStream)
        {
            var serializer = new DataContractJsonSerializer(typeof(DCP));
            var loadedDCPs = serializer.ReadObject(fileStream) as DCP;
            //DCPs.Clear();
            DCPs.Add(loadedDCPs);
            dataGridDCP.Items.Refresh();
        }

        private void dataGridDCP_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGridDCP.SelectedItem is DCP dcp)
            {
                buttonPlus.IsEnabled = true;
                dataGridLine.ItemsSource = dcp.Lines;
            }
            else
            {
                buttonPlus.IsEnabled = false;
            }
        }

        DCP ProcessDCP()
        {
            dataGridLine.CommitEdit(DataGridEditingUnit.Row, true);
            dataGridDCP.CommitEdit(DataGridEditingUnit.Row, true);

            if (dataGridDCP.SelectedItem is DCP dcp)
            {
                return dcp;
            }
            else
            {
                MessageBox.Show("Please select a DCP first");
                return null;
            }
        }

        private async void menuItemUpload_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessDCP() is DCP dcp)
            {
                progressBar.IsIndeterminate = true;

                try
                {
                    var resUrl = await DCPInterface.UploadDCP(dcp);
                    progressBar.Value = 100;

                    var webWindow = new BrowserWindow(resUrl);
                    webWindow.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error uploading DCP, " + ex.Message);
                    progressBar.Value = 0;
                }

                progressBar.IsIndeterminate = false;

            }
        }

        private void menuItemExport_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessDCP() is DCP dcp)
            {
                var template = File.ReadAllText("dcp.cshtml");
                var res = Engine.Razor.RunCompile(template, "key", typeof(DCP), dcp);

                var fileName = "dcp_" + dcp.DataTrasmissione.ToString("dd-MM-yyyy") + ".html";
                File.WriteAllText(fileName, res);
                Process.Start(fileName);
            }

        }

        private async void menuItemOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            await DCPInterface.Login();

            var w = new BrowserWindow("https://www.intranetssl.rai.it/,DanaInfo=.addrCwjx2q8sK3nwOy-+RicercaDCP.aspx");
            w.Show();
        }

        async Task ShowLogin()
        {
            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() ?? false)
            {
                progressBar.IsIndeterminate = true;
                var loginRes = await DCPInterface.Login(loginWindow.User, loginWindow.Password);
                if (!loginRes)
                {
                    MessageBox.Show("Could not log in (username or password wrong ?)");
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = 0;
                    return;
                }

                var licenseManager = new LicenseManager("http://vserver.pennpro.it:5000", "Ultrageheimespasswortafdesniadraufkimmsch");
                var authorized = await licenseManager.VerifyLicense(loginWindow.User);
                if (!authorized)
                {
                    MessageBox.Show("No License for user " + loginWindow.User + " found");
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = 0;
                    return;
                }

                Settings.Default.Username = loginWindow.User;
                Settings.Default.Password = loginWindow.Password;
                Settings.Default.Save();

                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;

                MessageBox.Show("Successfully logged in");
            }
        }

        private async void menuItemSettings_Click(object sender, RoutedEventArgs e)
        {
            await ShowLogin();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog();
            fileDialog.Filter = "Music|*.mp3;*.wav";
            fileDialog.Multiselect = true;

            if (fileDialog.ShowDialog() ?? false)
            {
                OpenFiles(fileDialog.FileNames);
            }
        }
    }
}
