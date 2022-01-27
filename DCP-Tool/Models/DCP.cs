using System;
using System.Collections.Generic;
using System.Linq;

namespace DCP_Tool
{
    public class DCP
    {
        public DateTime DataTrasmissione { get; set; } = DateTime.Now;
        public int Puntata { get; set; }

        private DateTime? _DataRegistrazione = null;
        public DateTime DataRegistrazione
        {
            get => _DataRegistrazione ?? DataTrasmissione - TimeSpan.FromDays(5);
            set => _DataRegistrazione = value;
        }

        public TimeSpan Durata { get; set; }
        public Sede Sede { get; set; }

        public string TitoloProgramma { get; set; }
        public string TitoloSerie { get; set; }
        private string _TitoloItaliano = null;
        public string TitoloItaliano
        {
            get => _TitoloItaliano ?? TitoloProgramma + " - " + TitoloSerie;
            set => _TitoloItaliano = value;
        }
        public string TitoloOriginale { get; set; }
        public string Sottotitolo { get; set; }

        public ReteTrasmissione ReteTrasmissione { get; set; }
        public string Societa { get; set; }

        public int Uorg { get; set; }
        public int Matricola { get; set; }
        public int Serie { get; set; }

        public string NumeroContratto { get; set; }
        public DateTime DataContratto { get; set; }
        public string DF { get; set; }

        public string Regista { get; set; }

        // Not exactly a timespan but there is no 'Time' class in C#
        public TimeSpan OraInizio { get; set; }

        public TimeSpan OraFine
        {
            get => OraInizio + Durata;
        }

        public List<DCPLine> Lines = new List<DCPLine>();
        public string GetPaperDCPValue(object obj)
        {
            var attributes = obj.GetType().GetMember(obj.ToString())[0].GetCustomAttributes(typeof(PaperDCPValueAttribute), false);
            if (attributes.Length > 0)
            {
                var att = (PaperDCPValueAttribute)attributes[0];
                return att.Value;
            }
            return obj.ToString();
        }

    }

    public class DCPLine
    {
        public string Titolo { get; set; } = "";

        public Autore[] Autori;
        public string AutoriString
        {
            get => Autori
                ?.Select(a => a.ToString())
                ?.Aggregate((s1, s2) => s1 + ", " + s2)
                ?? "";

            set => Autori = value
                ?.Split(',')
                ?.Select(a => new Autore(a.Trim()))
                ?.ToArray();
        }

        public TimeSpan Durata { get; set; }

        public GenereSIAE Gensiae { get; set; }
        public Ruolo Ruolo { get; set; }

        // All caps
        public string Marca { get; set; } = "";

        public string SiglaNum { get; set; } = "";
        public string Esecutori { get; set; } = "";

        public TipoGenerazione TipoGenerazione { get; set; }
    }

    public class PaperDCPValueAttribute : Attribute
    {
        public string Value;
        public PaperDCPValueAttribute(string value)
        {
            Value = value;
        }
    }

    public struct Autore
    {
        public String Nome;
        public String Cognome;

        public Autore(string nome, string cognome)
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
    
}
