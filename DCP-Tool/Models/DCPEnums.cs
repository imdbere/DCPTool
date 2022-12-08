namespace DCP_Tool.Models
{
    public enum GenereSiae
    {
        Ml
    }

    public enum Ruolo
    {
        Pp,
        Sf,
        Si
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
        Tv1,
        Tv2,
        Tv3,
        Tv4
    }

    public enum Sede
    {
        [PaperDcpValue("PROG. L. TED.   BZ/16")]
        BolzanoDe = 16,

        [PaperDcpValue("PROG. L. IT.   BZ/14")]
        BolzanoIt = 14,

        [PaperDcpValue("PROG. L. LAD.   BZ/15")]
        BolzanoLad = 15
    }

}