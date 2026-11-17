using DcpTool.Core.Model;
using DcpTool.Core.Validation;
using DcpTool.Core.Workflow;
using Xunit;

namespace DcpTool.Core.Tests;

public class WorkflowTests
{
    private static Dcp Header() => new()
    {
        TitoloItaliano = "Kompass",
        Sede = "BZ/16",
        Uorg = 9016,
        Matricola = 1,
        Durata = TimeSpan.FromMinutes(25),
    };

    private static DcpLine Line(string titolo, bool complete = true) => new()
    {
        Titolo = titolo,
        Autori = complete ? [new Autore("Anna", "Rossi")] : [],
        Durata = TimeSpan.FromSeconds(72),
        Marca = "Sonoton",
    };

    private static WorkflowState Evaluate(Dcp dcp) =>
        DcpWorkflow.Evaluate(dcp, DcpValidator.Validate(dcp, ValidationPurpose.Upload));

    [Fact]
    public void StepsFollowTheDcp()
    {
        Assert.Equal(PrimaryAction.CompleteHeader, Evaluate(new Dcp()).Action);

        var dcp = Header();
        Assert.Equal((WorkflowStep.AddMusic, PrimaryAction.AddMusic), (Evaluate(dcp).Step, Evaluate(dcp).Action));

        dcp.Lines.Add(Line("A", complete: false));
        var review = Evaluate(dcp);
        Assert.Equal(WorkflowStep.Review, review.Step);
        Assert.Equal(PrimaryAction.FixProblems, review.Action);
        Assert.True(review.Errors > 0);

        dcp.Lines[0].Autori = [new Autore("Anna", "Rossi")];
        Assert.Equal(PrimaryAction.Upload, Evaluate(dcp).Action);

        dcp.Upload = new UploadRecord { Complete = false, LinesSaved = 4 };
        Assert.Equal(PrimaryAction.ContinueUpload, Evaluate(dcp).Action);

        dcp.Upload = new UploadRecord { Complete = true, LinesSaved = 1, ContentHash = DcpFingerprint.Compute(dcp) };
        var done = Evaluate(dcp);
        Assert.Equal((WorkflowStep.Verify, PrimaryAction.OpenAtRai), (done.Step, done.Action));
        Assert.False(done.ChangedSinceUpload);
    }

    [Fact]
    public void TotalsCompareMusicWithTheProgramme()
    {
        var dcp = Header();
        dcp.Lines.Add(Line("A"));
        dcp.Lines.Add(Line("B"));
        var state = Evaluate(dcp);
        Assert.Equal(TimeSpan.FromSeconds(144), state.MusicTotal);
        Assert.Equal(144.0 / 1500, state.MusicShare!.Value, 6);
        Assert.Null(Evaluate(new Dcp()).MusicShare);
    }

    [Fact]
    public void OnlyUploadedFieldsCountAsChanges()
    {
        var dcp = Header();
        dcp.Lines.Add(Line("A"));
        dcp.Upload = new UploadRecord { Complete = true, LinesSaved = 1, ContentHash = DcpFingerprint.Compute(dcp) };

        dcp.NumeroContratto = "123";
        dcp.ResponsabileRai = "Someone";
        dcp.Lines[0].Durata += TimeSpan.FromMilliseconds(300); // RAI only takes whole seconds
        Assert.False(DcpFingerprint.ChangedSinceUpload(dcp));

        dcp.Lines[0].Titolo = "A (edit)";
        Assert.True(DcpFingerprint.ChangedSinceUpload(dcp));
        Assert.Contains(DcpValidator.Validate(dcp, ValidationPurpose.Upload), i => i.Code == "changed-since-upload" && !i.IsError);
        Assert.True(Evaluate(dcp).ChangedSinceUpload);
    }

    [Fact]
    public void OldRecordsWithoutFingerprintNeverCountAsChanged()
    {
        var dcp = Header();
        dcp.Upload = new UploadRecord { Complete = true };
        dcp.TitoloItaliano = "Other";
        Assert.False(DcpFingerprint.ChangedSinceUpload(dcp));
    }
}
