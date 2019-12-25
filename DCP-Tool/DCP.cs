using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace DCP_Tool
{
    public class DCP
    {
        //public string CodiceDCP;
        public DateTime DataTrasmissione { get; set; } = DateTime.Now;
        public string TitoloItaliano { get; set; }
        public string TitoloOriginale { get; set; }
        public ReteTrasmissione ReteTrasmissione { get; set; }
        public Sede Sede { get; set; }
        public string Struttura { get; set; }
        public string Sottotitolo { get; set; }

        public int Uorg { get; set; }
        public int Matricola { get; set; }
        public int Serie { get; set; }
        public int Puntata { get; set; }

        public DateTime? _DataRegistrazione = null;
        public DateTime DataRegistrazione
        {
            get => _DataRegistrazione ?? DataTrasmissione - TimeSpan.FromDays(5);
            set => _DataRegistrazione = value;
        }

        // Not exactly a timespan but there is no 'Time' class in C#
        public TimeSpan OraInizio { get; set; }
        public TimeSpan Durata { get; set; }

        private TimeSpan OraFine
        {
            get => OraInizio + Durata;
        }

        public List<DCPLine> Lines = new List<DCPLine>();

        public void ParseDataTable(HtmlAgilityPack.HtmlNode node)
        {

        }

        public Dictionary<string, string> GetBasicFormData()
        {
            return new Dictionary<string, string>()
            {
                { "TextBox_TitoloIta", TitoloItaliano},
                { "TextBox_TitoloOri", TitoloOriginale},
                { "TextBox_SottotitoloPuntata", Sottotitolo },
                { "TextBox_TitoloAgg", "" },
                { "DateControl_DataRegistrazione:Hidden_Errore", "" },
                { "DateControl_DataRegistrazione:TextBox_Data", DataRegistrazione.ToDCPString() },
                { "DateControl_DataRegistrazione:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm" },
                { "TimeControl_DurataTx:hh", Durata.Hours.ToString("00")},
                { "TimeControl_DurataTx:mm", Durata.Minutes.ToString("00")},
                { "TimeControl_DurataTx:ss", Durata.Seconds.ToString("00")},
                { "TimeControl_DurataTx:Hidden_Info", "true#false" },
                { "TimeControl_DurataTx:hidden_TvRf", "" },
                { "TimeControl_DurataTx:Hidden_Modify", "false" },
                { "TimeControl_DurataTx:Hidden_ReadOnly", "true#false" },
                { "DateControl_DataTrasmissione:Hidden_Errore", "" },
                { "DateControl_DataTrasmissione:TextBox_Data", DataTrasmissione.ToDCPString() },
                { "DateControl_DataTrasmissione:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm" },
                { "TimeControl_OraInizio:hh", OraInizio.Hours.ToString("00")},
                { "TimeControl_OraInizio:mm", OraInizio.Minutes.ToString("00")},
                { "TimeControl_OraInizio:ss", OraInizio.Seconds.ToString("00")},
                { "TimeControl_OraInizio:Hidden_Info", "true#false" },
                { "TimeControl_OraInizio:hidden_TvRf", "TV" },
                { "TimeControl_OraInizio:Hidden_Modify", "false" },
                { "TimeControl_OraInizio:Hidden_ReadOnly", "true#false" },
                { "TimeControl_OraFine:hh", OraFine.Hours.ToString("00")},
                { "TimeControl_OraFine:mm", OraFine.Minutes.ToString("00")},
                { "TimeControl_OraFine:ss", OraFine.Seconds.ToString("00")},
                { "TimeControl_OraFine:Hidden_Info", "true#false" },
                { "TimeControl_OraFine:hidden_TvRf", "" },
                { "TimeControl_OraFine:Hidden_Modify", "false" },
                { "TimeControl_OraFine:Hidden_ReadOnly", "true#false" },
                { "APControl_UMSP:UorgTxt", Uorg != 0 ? Uorg.ToString() : "" },
                { "APControl_UMSP:MatricolaTxt", Matricola != 0 ? Matricola.ToString() : "" },
                { "APControl_UMSP:SerieTxt", Serie != 0 ? Serie.ToString() : "" },
                { "APControl_UMSP:PuntataTxt", Puntata != 0 ? Puntata.ToString() : "" },
                { "Textbox_Committente", Struttura },
                { "CheckControl_Struttura:TextBox1", ReteTrasmissione.ToString() },
                { "CheckControl_Struttura:TextBox_NomeTabella", "TabStrutture" },
                { "CheckControl_Struttura:TextBox_Errore", "" },
                { "CheckControl_Struttura:TextBox_Dimensioni", "70#100" },
                { "DropDownList_SediPool", ((int)Sede).ToString() },
                { "DateControl_DataRepl:Hidden_Errore", "" },
                { "DateControl_DataRepl:TextBox_Data",  DataTrasmissione.ToDCPString() },
                { "DateControl_DataRepl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm" },
                { "Hidden_InfoBack", "" },
                { "TextBox_Note", "" },
                { "flagSposta", "0" },
                { "flagCopia", "0" },
                { "flagSalva", "0" },
                { "flagCerca", "0" },
                { "sedeutente", "" },
                { "rete", "" },
                { "data", "" },
                { "idtrasmissionescelta", "" },
                { "statotx", "" },
                { "opereInserto", "" },
                { "CommandBarControl1:ImageButton_Salva.x", "17" },
                { "CommandBarControl1:ImageButton_Salva.y", "30" }
            };
        }

        public Dictionary<string, string> GetLineFormData(int offset, int count)
        {
            var dict = new Dictionary<string, string>();

            if (Lines.Any(l => l.Durata == default))
            {
                throw new Exception("Durata missing on some lines");
            }

            var linesToTake = Lines.Skip(offset).Take(count);
            int i = offset;
            foreach(var line in linesToTake)
            {
                var lineDict = line.ToFormData(i++);
                foreach(var kv in lineDict)
                {
                    dict.Add(kv.Key, kv.Value);
                }
            }

            // Fill with empty lines until there are at least 4 lines
            if (i - offset < count)
            {
                for (int j = i; j < count + offset; j++)
                {
                    var emptyLine = new DCPLine();
                    var fd = emptyLine.ToFormData(j);
                    foreach (var kv in fd)
                    {
                        dict.Add(kv.Key, kv.Value);
                    }
                }
            }

            return dict;
        }

    }

    public enum ReteTrasmissione
    {
        TV1,
        TV2,
        TV3,
        TV4
    }
    public enum Sede
    {
        Bolzano_DE = 16,
        Bolzano_IT = 14,
        Bolzano_LAD = 15
    }

    public class DCPLine
    {
        public string Titolo { get; set; } = "";

        private Autore[] Autori;
        public string AutoriString
        {
            get => Autori
                .Select(a => a.ToString())
                .Aggregate((s1, s2) => s1 + ", " + s2);

            set => Autori = value
                .Split(',')
                .Select(a => new Autore(a.Trim()))
                .ToArray();
        }

        public TimeSpan Durata { get; set; }

        public GenereSIAE? Gensiae { get; set; }
        public Ruolo Ruolo { get; set; }

        // All caps
        public string Marca { get; set; } = "";

        public string SiglaNum { get; set; } = "";
        public string Esecutori { get; set; } = "";

        public TipoGenerazione? TipoGenerazione { get; set; }

        private string NomeString
        {
            get => Autori == null ? "1#%#" :
                $"{ Autori.Length }#" + 
                Autori
                    .Select(a => $"{a.Cognome.Replace(' ', '+')}%{a.Nome.Replace(' ', '+')}#")
                    .Aggregate((s1, s2) => s1 + s2);
        }
        public Dictionary<string, string> ToFormData(int nr)
        {
            var index = nr + 2;
            return new Dictionary<string, string>()
            {
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox1", Gensiae?.ToString() ?? ""},
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox_NomeTabella",  "TabGenereSIAE"},
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox_Dimensioni",  "60#60"},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox1",  Ruolo.ToString()},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox_NomeTabella",  "TabRuolo"},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox_Dimensioni",  "60#178"},
                {$"DataGrid1:_ctl{index}:TextBox_TitoloOpera", Titolo},
                {$"DataGrid1:_ctl{index}:AuthorsControl_AutoriOpera:HiddenText", NomeString /*"1#test%test1#"*/},
                {$"DataGrid1:_ctl{index}:AuthorsControl_AutoriOpera:Hidden_Info",  "true#false"},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:hh",  Durata.Hours.ToString("00")},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:mm",  Durata.Minutes.ToString("00")},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:ss",  Durata.Seconds.ToString("00")},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:Hidden_Info",  "False#True"},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:hidden_TvRf", ""},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:hdnCrono",  "Start"},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox1", TipoGenerazione != null ? ((int)TipoGenerazione).ToString() : ""},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_NomeTabella",  "TabTipoGenerazione"},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_Dimensioni",  "82#140"},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_Mezzo", ""},
                {$"DataGrid1:_ctl{index}:CheckControlMarca_MarcaOpera:TextBox1", Marca.Trim().ToUpper()},
                {$"DataGrid1:_ctl{index}:CheckControlMarca_MarcaOpera:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControlMarca_MarcaOpera:TextBox_Dimensioni",  "70#100"},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:TextBox1", ""},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:TextBox_Dimensioni",  "70#110"},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:Edizione_esiste", ""},
                {$"DataGrid1:_ctl{index}:TextBox_SiglaNum", SiglaNum},
                {$"DataGrid1:_ctl{index}:TextBox_NoteOpera", ""},
                {$"DataGrid1:_ctl{index}:TextBox_EsecutoriOpera", Esecutori},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox1", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox_NomeTabella",  "TabProprieta"},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox_Dimensioni",  "82#140"},
                {$"DataGrid1:_ctl{index}:TextBox_DataProduz", ""},
            };
        }
    }

    public struct Autore
    {
        public String Nome;
        public String Cognome;

        public Autore (string nome, string cognome)
        {
            Nome = nome;
            Cognome = cognome;
        }

        public Autore(string nome)
        {
            var s = nome.Split(' ');
            Nome = s[0];
            if (s.Length > 1)
                Cognome = s[1];
            else
                Cognome = "";
        }

        public override string ToString()
        {
            return Nome + " " + Cognome;
        }
    }
    
    public enum GenereSIAE
    {
        ML
    }

    public enum Ruolo
    {
        PP,
        SF,
        SI
    }

    public enum TipoGenerazione
    {
        OperaSuDisco = 9
    }

    public static class Extensions
    {
        public static string ToDCPString(this DateTime date)
        {
            return date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
