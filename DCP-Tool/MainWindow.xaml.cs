using DCP_Tool.Properties;
using Microsoft.Win32;
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
using DCP_Tool.Helpers;
using DCP_Tool.Models;

namespace DCP_Tool
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly List<Dcp> _dcPs = new List<Dcp>();
        private readonly DcpInterface _dcpInterface;

        public MainWindow(string file)
        {
            InitializeComponent();

            this.Title = "DCP Tool " + System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
            dataGridDCP.ItemsSource = _dcPs;
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

            _dcpInterface = new DcpInterface(Settings.Default.Username, Settings.Default.Password);

            if (file != null)
            {
                LoadDcpFile(File.OpenRead(file));
            }

            if (string.IsNullOrEmpty(Settings.Default.Username) || string.IsNullOrEmpty(Settings.Default.Password))
            {
                ShowLogin();
            }
        }

        private async Task<DcpLine> ReadDcpLineFromFile(string filename)
        {
            var fileStream = File.OpenRead(filename);

            var tagFile = TagLib.File.Create(new TagLib.StreamFileAbstraction(filename, fileStream, fileStream));
            var tags = tagFile.GetTag(TagLib.TagTypes.Id3v2);
            using var sonofindInterface = new SonofindInterface();

            var cdTitle = (new FileInfo(filename)).Name.Split('_')[0];
            var sonofindRes = await sonofindInterface.QueryTitle(cdTitle);

            var line = new DcpLine()
            {
                AutoriString = tags.FirstComposer,
                Marca = sonofindRes != null ? "Sonoton" : "",
                Esecutori = sonofindRes?.Artists ?? tags.Performers.Aggregate((s1, s2) => s1 + ", " + s2),
                Titolo = sonofindRes?.Title ?? tags.Title,
                Durata = tagFile.Properties.Duration,
                SiglaNum = sonofindRes != null ? tags.Album.Split('-')[0] : tags.Album,
                TipoGenerazione = TipoGenerazione.OperaSuDisco,
                Ruolo = Ruolo.SF,
                Gensiae = GenereSiae.ML
            };

            return line;
        }

        private async void OpenFiles(string[] files)
        {
            progressBar.IsIndeterminate = true;
            var dcpList = await Task.WhenAll(files.Select(f => ReadDcpLineFromFile(f)));

            if (dataGridDCP.SelectedItem is Dcp dcp)
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

                var serializer = new DataContractJsonSerializer(typeof(Dcp));
                dataGridLine.CommitEdit();
                dataGridDCP.CommitEdit();
                if (dataGridDCP.SelectedItem is Dcp dcp)
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

            if (dialog.ShowDialog() ?? false)
            {
                var fileStream = dialog.OpenFile();
                LoadDcpFile(fileStream);
            }
        }

        private void LoadDcpFile(Stream fileStream)
        {
            var serializer = new DataContractJsonSerializer(typeof(Dcp));
            var loadedDcPs = serializer.ReadObject(fileStream) as Dcp;

            _dcPs.Add(loadedDcPs);
            dataGridDCP.Items.Refresh();
        }

        private void dataGridDCP_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGridDCP.SelectedItem is Dcp dcp)
            {
                buttonPlus.IsEnabled = true;
                dataGridLine.ItemsSource = dcp.Lines;
            }
            else
            {
                buttonPlus.IsEnabled = false;
            }
        }

        private Dcp ProcessDcp()
        {
            dataGridLine.CommitEdit(DataGridEditingUnit.Row, true);
            dataGridDCP.CommitEdit(DataGridEditingUnit.Row, true);

            if (dataGridDCP.SelectedItem is Dcp dcp)
            {
                return dcp;
            }
            
            MessageBox.Show("Please select a DCP first");
            return null;
        }

        private async void menuItemUpload_Click(object sender, RoutedEventArgs e)
        {
            var dcp = ProcessDcp();
            if (dcp != null)
            {
                progressBar.IsIndeterminate = true;

                try
                {
                    var resUrl = await _dcpInterface.UploadDcp(dcp);
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
            var dcp = ProcessDcp();
            if (dcp != null)
            {
                var dialog = new SaveFileDialog();
                var defaultFileName = "dcp_" + dcp.DataTrasmissione.ToString("dd-MM-yyyy") + ".docx";
                
                dialog.Filter = "Word | *.docx";
                dialog.DefaultExt = "docx";
                dialog.FileName = defaultFileName;

                if (dialog.ShowDialog() ?? false)
                {
                    var fileName = dialog.FileName;
                    var documentWriter = new DocumentWriter();
                    
                    documentWriter.GenerateDocument(dcp, fileName);
                    Tools.OpenFile(fileName);
                }
            }

        }

        private async void menuItemOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            await _dcpInterface.Login();

            var w = new BrowserWindow($"https://www.intranetssl.rai.it/,DanaInfo=.{DcpInterface.DanaInfo}+RicercaDCP.aspx");
            w.Show();
        }

        private async Task ShowLogin()
        {
            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() ?? false)
            {
                progressBar.IsIndeterminate = true;
                try
                {
                    await _dcpInterface.Login(loginWindow.User, loginWindow.Password);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not log in (username or password wrong ?)");
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
