namespace DCP_Tool
{
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
        [PaperDCPValue("C")]
        OperaSuDisco = 9,
        [PaperDCPValue("")]
        DalVivo = 1
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
        [PaperDCPValue("PROG. L. TED.   BZ/16")]
        Bolzano_DE = 16,

        [PaperDCPValue("PROG. L. IT.   BZ/14")]
        Bolzano_IT = 14,

        [PaperDCPValue("PROG. L. LAD.   BZ/15")]
        Bolzano_LAD = 15
    }

}