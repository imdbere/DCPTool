namespace DCP_Tool.Models
{
    public enum GenereSiae
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
        [PaperDcpValue("C")]
        OperaSuDisco = 9,
        [PaperDcpValue("")]
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
        [PaperDcpValue("PROG. L. TED.   BZ/16")]
        Bolzano_DE = 16,

        [PaperDcpValue("PROG. L. IT.   BZ/14")]
        Bolzano_IT = 14,

        [PaperDcpValue("PROG. L. LAD.   BZ/15")]
        Bolzano_LAD = 15
    }

}