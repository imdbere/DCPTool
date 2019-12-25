using DCP_Tool.Properties;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using RazorEngine;
using RazorEngine.Templating; // For extension methods.

namespace DCP_Tool
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<DCP> DCPs = new List<DCP>();

        public MainWindow()
        {
            InitializeComponent();
            dataGridDCP.ItemsSource = DCPs;
        }

        async Task<DCPLine> ReadDCPLineFromFile(string filename)
        {
            var fileStream = File.OpenRead(filename);

            var tagFile = TagLib.File.Create(new TagLib.StreamFileAbstraction(filename, fileStream, fileStream));
            var tags = tagFile.GetTag(TagLib.TagTypes.Id3v2);
            var sonofindInterface = new SonofindInterface();

            var cdTitle = (new FileInfo(filename)).Name.Split('_')[0];
            var sonofindRes = await sonofindInterface.QueryTitle(cdTitle);

            var line = new DCPLine()
            {
                AutoriString = tags.FirstComposer,
                Marca = "Sonoton",
                Esecutori = sonofindRes.artists, //tags.Performers.Aggregate((s1, s2) => s1 + ", " + s2),
                Titolo = tags.Title.Split(new string[] { "---" }, StringSplitOptions.None)[0].Trim(),
                Durata = tagFile.Properties.Duration,
                SiglaNum = tags.Album.Split('-')[0],
                TipoGenerazione = TipoGenerazione.OperaSuDisco,
                Ruolo = Ruolo.SF,
                Gensiae = GenereSIAE.ML
            };

            return line;
        }

        async void OpenFiles(string[] files)
        {
            var dcpLists = new List<DCPLine>();

            foreach(var file in files)
            {
                dcpLists.Add(await ReadDCPLineFromFile(file));
            }

            if (dataGridDCP.SelectedItem is DCP dcp)
            {
                dcp.Lines.AddRange(dcpLists);
                dataGridLine.Items.Refresh();
            }

        }

        private void Image_Drop(object sender, DragEventArgs e)
        {

        }

        private void Rectangle_Drop(object sender, DragEventArgs e)
        {

        }

        private void dataGridDCP_CurrentCellChanged(object sender, EventArgs e)
        {

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
            //dialog.Multiselect = true;
            //dialog.Filter = "*.mp3";

            if (dialog.ShowDialog() ?? false)
            {
                var fileStream = dialog.OpenFile();

                var serializer = new DataContractJsonSerializer(typeof(DCP));
                var loadedDCPs = serializer.ReadObject(fileStream) as DCP;
                //DCPs.Clear();
                DCPs.Add(loadedDCPs);
                dataGridDCP.Items.Refresh();
            }
        }

        private void dataGridDCP_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGridDCP.SelectedItem is DCP dcp)
            {
                dataGridLine.ItemsSource = dcp.Lines;
            }
        }

        private async void menuItemUpload_Click(object sender, RoutedEventArgs e)
        {
            dataGridLine.CommitEdit();
            dataGridDCP.CommitEdit();

            if (dataGridDCP.SelectedItem is DCP dcp)
            {
                progressBar.IsIndeterminate = true;
                var dcpInterface = new DCPInterface(Settings.Default.Username, Settings.Default.Password);
                try
                {
                    await dcpInterface.LoginAndUpload(dcp);
                    progressBar.Value = 100;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error uploading DCP, " + ex.Message);
                    progressBar.Value = 0;
                }

                progressBar.IsIndeterminate = false;
                
            }
            else
            {
                MessageBox.Show("Please select a DCP first");
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var dcp = new DCP()
            {
                DataTrasmissione = DateTime.Now,
                TitoloOriginale = "TestUpload",
                TitoloItaliano = "TestUploadI",
                Durata = new TimeSpan(0, 30, 0),
                OraInizio = new TimeSpan(18, 0, 0),
                ReteTrasmissione = ReteTrasmissione.TV4,
                Sede = Sede.Bolzano_DE,

                Uorg = 9016,
                Matricola = 486840,
                Puntata = 36
            };

            dcp.Lines.Add(new DCPLine()
            {
                AutoriString = "Aaron Penn, Georg Penn",
                Durata = new TimeSpan(0, 2, 0),
                Titolo = "TestMusic",
                Esecutori = "Aaron Penn Productions",
                Gensiae = GenereSIAE.ML,
                Marca = "Sonoton",
                Ruolo = Ruolo.PP,
                SiglaNum = "SCD1234",
                TipoGenerazione = TipoGenerazione.OperaSuDisco
            });

            var dcpInterface = new DCPInterface(Settings.Default.Username, Settings.Default.Password);
            await dcpInterface.LoginAndUpload(dcp);
        }

        private void menuItemExport_Click(object sender, RoutedEventArgs e)
        {
            var template = File.ReadAllText("dcp.cshtml");
            var res = Engine.Razor.RunCompile(template, "key", null, new { Hallo = "Welt" });
        }
    }
}
