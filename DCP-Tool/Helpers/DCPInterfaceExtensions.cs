using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DCP_Tool.Models;

namespace DCP_Tool.Helpers
{
    public static class DcpInterfaceExtensions
    {
        private static string GetAutoriString(Autore[] autori)
        {
            return autori == null ? "1#%#" :
                $"{ autori.Length }#" +
                autori
                    .Select(a => $"{a.Cognome.Replace(' ', '+')}%{a.Nome.Replace(' ', '+')}#")
                    .Aggregate((s1, s2) => s1 + s2);
        }
        public static Dictionary<string, string> ToFormData(this DcpLine line, int nr, bool emptyLine = false)
        {
            var index = nr + 2;
            return new Dictionary<string, string>()
            {
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox1", emptyLine ? "" : line.Gensiae.ToString()},
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox_NomeTabella",  "TabGenereSIAE"},
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_GenereSIAE:TextBox_Dimensioni",  "60#60"},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox1",  line.Ruolo.ToString()},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox_NomeTabella",  "TabRuolo"},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_Ruolo:TextBox_Dimensioni",  "60#178"},
                {$"DataGrid1:_ctl{index}:TextBox_TitoloOpera", line.Titolo},
                {$"DataGrid1:_ctl{index}:AuthorsControl_AutoriOpera:HiddenText", GetAutoriString(line.Autori) /*"1#test%test1#"*/},
                {$"DataGrid1:_ctl{index}:AuthorsControl_AutoriOpera:Hidden_Info",  "true#false"},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:hh",  line.Durata.Hours.ToString("00")},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:mm",  line.Durata.Minutes.ToString("00")},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:ss",  line.Durata.Seconds.ToString("00")},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:Hidden_Info",  "False#True"},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:hidden_TvRf", ""},
                {$"DataGrid1:_ctl{index}:TimeControl_Durata:hdnCrono",  "Start"},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox1", emptyLine ? "" : ((int)line.TipoGenerazione).ToString()},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_NomeTabella",  "TabTipoGenerazione"},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_Dimensioni",  "82#140"},
                {$"DataGrid1:_ctl{index}:CheckControl_TipoGenerazione:TextBox_Mezzo", ""},
                {$"DataGrid1:_ctl{index}:CheckControlMarca_MarcaOpera:TextBox1", line.Marca.Trim().ToUpper()},
                {$"DataGrid1:_ctl{index}:CheckControlMarca_MarcaOpera:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControlMarca_MarcaOpera:TextBox_Dimensioni",  "70#100"},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:TextBox1", ""},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:TextBox_Dimensioni",  "70#110"},
                {$"DataGrid1:_ctl{index}:CheckControlEdizione_EdizioneOpera:Edizione_esiste", ""},
                {$"DataGrid1:_ctl{index}:TextBox_SiglaNum", line.SiglaNum},
                {$"DataGrid1:_ctl{index}:TextBox_NoteOpera", ""},
                {$"DataGrid1:_ctl{index}:TextBox_EsecutoriOpera", line.Esecutori},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox1", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox_NomeTabella",  "TabProprieta"},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox_Errore", ""},
                {$"DataGrid1:_ctl{index}:CheckControl_Proprieta:TextBox_Dimensioni",  "82#140"},
                {$"DataGrid1:_ctl{index}:TextBox_DataProduz", ""},
            };
        }
        
        public static Dictionary<string, string> GetBasicFormData(this Dcp dcp)
        {
            return new Dictionary<string, string>()
            {
                { "TextBox_TitoloIta", dcp.TitoloItaliano},
                { "TextBox_TitoloOri", dcp.TitoloOriginale},
                { "TextBox_SottotitoloPuntata", dcp.Sottotitolo },
                { "TextBox_TitoloAgg", "" },
                { "DateControl_DataRegistrazione:Hidden_Errore", "" },
                { "DateControl_DataRegistrazione:TextBox_Data", dcp.DataRegistrazione.ToDcpString() },
                { "DateControl_DataRegistrazione:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm" },
                { "TimeControl_DurataTx:hh", dcp.Durata.Hours.ToString("00")},
                { "TimeControl_DurataTx:mm", dcp.Durata.Minutes.ToString("00")},
                { "TimeControl_DurataTx:ss", dcp.Durata.Seconds.ToString("00")},
                { "TimeControl_DurataTx:Hidden_Info", "true#false" },
                { "TimeControl_DurataTx:hidden_TvRf", "" },
                { "TimeControl_DurataTx:Hidden_Modify", "false" },
                { "TimeControl_DurataTx:Hidden_ReadOnly", "true#false" },
                { "DateControl_DataTrasmissione:Hidden_Errore", "" },
                { "DateControl_DataTrasmissione:TextBox_Data", dcp.DataTrasmissione.ToDcpString() },
                { "DateControl_DataTrasmissione:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm" },
                { "TimeControl_OraInizio:hh", dcp.OraInizio.Hours.ToString("00")},
                { "TimeControl_OraInizio:mm", dcp.OraInizio.Minutes.ToString("00")},
                { "TimeControl_OraInizio:ss", dcp.OraInizio.Seconds.ToString("00")},
                { "TimeControl_OraInizio:Hidden_Info", "true#false" },
                { "TimeControl_OraInizio:hidden_TvRf", "TV" },
                { "TimeControl_OraInizio:Hidden_Modify", "false" },
                { "TimeControl_OraInizio:Hidden_ReadOnly", "true#false" },
                { "TimeControl_OraFine:hh", dcp.OraFine.Hours.ToString("00")},
                { "TimeControl_OraFine:mm", dcp.OraFine.Minutes.ToString("00")},
                { "TimeControl_OraFine:ss", dcp.OraFine.Seconds.ToString("00")},
                { "TimeControl_OraFine:Hidden_Info", "true#false" },
                { "TimeControl_OraFine:hidden_TvRf", "" },
                { "TimeControl_OraFine:Hidden_Modify", "false" },
                { "TimeControl_OraFine:Hidden_ReadOnly", "true#false" },
                { "APControl_UMSP:UorgTxt", dcp.Uorg != 0 ? dcp.Uorg.ToString() : "" },
                { "APControl_UMSP:MatricolaTxt", dcp.Matricola != 0 ? dcp.Matricola.ToString() : "" },
                { "APControl_UMSP:SerieTxt", dcp.Serie.ToString("000") },
                { "APControl_UMSP:PuntataTxt", dcp.Puntata != 0 ? dcp.Puntata.ToString() : "" },
                { "Textbox_Committente", "BZ/" + ((int)dcp.Sede).ToString() },
                { "CheckControl_Struttura:TextBox1", dcp.ReteTrasmissione.ToString() },
                { "CheckControl_Struttura:TextBox_NomeTabella", "TabStrutture" },
                { "CheckControl_Struttura:TextBox_Errore", "" },
                { "CheckControl_Struttura:TextBox_Dimensioni", "70#100" },
                { "DropDownList_SediPool", ((int)dcp.Sede).ToString() },
                { "DateControl_DataRepl:Hidden_Errore", "" },
                { "DateControl_DataRepl:TextBox_Data",  dcp.DataTrasmissione.ToDcpString() },
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

        public static Dictionary<string, string> GetSearchFormData(string reteTransmissione, DateTime startDate, DateTime endDate)
        {
            return new Dictionary<string, string>() {
                {"__EVENTTARGET", ""},
                {"__EVENTARGUMENT", ""},
                {"CheckControl_Stato:TextBox1", ""},
                {"CheckControl_Stato:TextBox_NomeTabella", "TabStati"},
                {"CheckControl_Stato:TextBox_Errore", ""},
                {"CheckControl_Stato:TextBox_Dimensioni", "54#80"},
                {"TextBox_CodiceDCP", ""},
                {"TextBox_VersioneDCP", ""},
                {"APControl1:UorgTxt", ""},
                {"APControl1:MatricolaTxt", ""},
                {"APControl1:SerieTxt", ""},
                {"APControl1:PuntataTxt", ""},
                {"TextBox_Identificatore", ""},
                {"TextBox_TitoloIta", ""},
                {"TextBox_TitoloOri", ""},
                {"TextBox_SottotitoloPuntata", ""},
                {"TextBox_TitoloAgg", ""},
                {"TextBox_Committente", ""},
                {"CheckControl_Struttura:TextBox1", reteTransmissione},
                {"CheckControl_Struttura:TextBox_NomeTabella", "TabStrutture"},
                {"CheckControl_Struttura:TextBox_Errore", ""},
                {"CheckControl_Struttura:TextBox_Dimensioni", "70#100"},
                {"TextBox_Responsabile", ""},
                {"TextBox_Curatore", ""},
                {"DateControl_RegistrazioneDal:Hidden_Errore", ""},
                {"DateControl_RegistrazioneDal:TextBox_Data", ""},
                {"DateControl_RegistrazioneDal:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_RegistrazioneAl:Hidden_Errore", ""},
                {"DateControl_RegistrazioneAl:TextBox_Data", ""},
                {"DateControl_RegistrazioneAl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_TxDal:Hidden_Errore", ""},
                {"DateControl_TxDal:TextBox_Data", startDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)},
                {"DateControl_TxDal:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_TxAl:Hidden_Errore", ""},
                {"DateControl_TxAl:TextBox_Data", endDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)},
                {"DateControl_TxAl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"TimeControl_OraInizioDalle:hh", "00"},
                {"TimeControl_OraInizioDalle:mm", "00"},
                {"TimeControl_OraInizioDalle:ss", "00"},
                {"TimeControl_OraInizioDalle:Hidden_Info", "true#false"},
                {"TimeControl_OraInizioDalle:hidden_TvRf", ""},
                {"TimeControl_OraInizioDalle:Hidden_Modify", "false"},
                {"TimeControl_OraInizioDalle:Hidden_ReadOnly", "true#false"},
                {"TimeControl_OraInizioAlle:hh", "00"},
                {"TimeControl_OraInizioAlle:mm", "00"},
                {"TimeControl_OraInizioAlle:ss", "00"},
                {"TimeControl_OraInizioAlle:Hidden_Info", "true#false"},
                {"TimeControl_OraInizioAlle:hidden_TvRf", ""},
                {"TimeControl_OraInizioAlle:Hidden_Modify", "false"},
                {"TimeControl_OraInizioAlle:Hidden_ReadOnly", "true#false"},
                {"TimeControl_DurataTrasmissione:hh", "00"},
                {"TimeControl_DurataTrasmissione:mm", "00"},
                {"TimeControl_DurataTrasmissione:ss", "00"},
                {"TimeControl_DurataTrasmissione:Hidden_Info", "true#True"},
                {"TimeControl_DurataTrasmissione:hidden_TvRf", ""},
                {"TimeControl_DurataTrasmissione:Hidden_Modify", "false"},
                {"TimeControl_DurataTrasmissione:Hidden_ReadOnly", "true#false"},
                {"DateControl_InserimentoDal:Hidden_Errore", ""},
                {"DateControl_InserimentoDal:TextBox_Data", ""},
                {"DateControl_InserimentoDal:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"DateControl_InserimentoAl:Hidden_Errore", ""},
                {"DateControl_InserimentoAl:TextBox_Data", ""},
                {"DateControl_InserimentoAl:Hidden_CalendarPopupUrl", "http://dcp.servizi.rai.it//UserControls/DateControl/DateControlCalendarPopup.htm"},
                {"TextBox_NoteDCP", ""},
                {"TextBox_TitoloOpera", ""},
                {"TimeControl_DurataOpera:hh", "00"},
                {"TimeControl_DurataOpera:mm", "00"},
                {"TimeControl_DurataOpera:ss", "00"},
                {"TimeControl_DurataOpera:Hidden_Info", "true#True"},
                {"TimeControl_DurataOpera:hidden_TvRf", ""},
                {"TimeControl_DurataOpera:Hidden_Modify", "false"},
                {"TimeControl_DurataOpera:Hidden_ReadOnly", "true#false"},
                {"CheckControl_GenSIAE:TextBox1", ""},
                {"CheckControl_GenSIAE:TextBox_NomeTabella", "TabGenereSIAE"},
                {"CheckControl_GenSIAE:TextBox_Errore", ""},
                {"CheckControl_GenSIAE:TextBox_Dimensioni", "60#60"},
                {"CheckControl_TGen:TextBox1", ""},
                {"CheckControl_TGen:TextBox_NomeTabella", "TabTipoGenerazione"},
                {"CheckControl_TGen:TextBox_Errore", ""},
                {"CheckControl_TGen:TextBox_Dimensioni", "82#140"},
                {"CheckControlMarca1:TextBox1", ""},
                {"CheckControlMarca1:TextBox_Errore", ""},
                {"CheckControlMarca1:TextBox_Dimensioni", "70#100"},
                {"TextBox_SiglaNumero", ""},
                {"CheckControlEdizione1:TextBox1", ""},
                {"CheckControlEdizione1:TextBox_Errore", ""},
                {"CheckControlEdizione1:TextBox_Dimensioni", "70#110"},
                {"CheckControlEdizione1:Edizione_esiste", ""},
                {"CheckControl_Proprieta:TextBox1", ""},
                {"CheckControl_Proprieta:TextBox_NomeTabella", "TabProprieta"},
                {"CheckControl_Proprieta:TextBox_Errore", ""},
                {"CheckControl_Proprieta:TextBox_Dimensioni", "82#140"},
                {"TextBox_Autori", ""},
                {"TextBox_Esecutori", ""},
                {"Textbox_NoteOpera", ""},
                {"ImageButton_Ricerca2.x", "20"},
                {"ImageButton_Ricerca2.y", "20"}
            };
        }

        public static Dictionary<string, string> GetLoginForm(string user, string password)
        {
            return new Dictionary<string, string>() {
                {"tz_offset", "60"},
                {"username", user},
                {"password", password},
                {"realm", "ADonly"},
                {"btnSubmit", "Entra"},
            };
        }

        public static Dictionary<string, string> GetLineFormData(this Dcp dcp, int offset, int count)
        {
            var dict = new Dictionary<string, string>();

            if (dcp.Lines.Any(l => l.Durata == default))
            {
                throw new Exception("Durata missing on some lines");
            }

            var linesToTake = dcp.Lines.Skip(offset).Take(count);
            int i = offset;
            foreach (var line in linesToTake)
            {
                var lineDict = line.ToFormData(i++);
                foreach (var kv in lineDict)
                {
                    dict.Add(kv.Key, kv.Value);
                }
            }

            // Fill with empty lines until there are at least 4 lines
            if (i - offset < count)
            {
                var emptyLine = new DcpLine();
                for (int j = i; j < count + offset; j++)
                {
                    var fd = emptyLine.ToFormData(j, true);
                    foreach (var kv in fd)
                    {
                        dict.Add(kv.Key, kv.Value);
                    }
                }
            }

            return dict;
        }

        private static string ToDcpString(this DateTime date)
        {
            return date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

    }
}