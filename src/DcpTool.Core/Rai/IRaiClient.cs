using System.Net;
using DcpTool.Core.Model;

namespace DcpTool.Core.Rai;

public sealed record RaiCredentials(string Username, string Password);

public enum RaiPage
{
    /// <summary>The form for a new DCP.</summary>
    DettaglioDcp,

    /// <summary>The DCP search.</summary>
    RicercaDcp,
}

public enum RaiErrorKind
{
    /// <summary>The portal refused username or password.</summary>
    InvalidCredentials,

    /// <summary>The portal asks for something this client can't provide: a second factor, a new password, a host check.</summary>
    InteractiveLoginRequired,

    /// <summary>The portal session ended in the middle of an operation and logging in again didn't help.</summary>
    SessionExpired,

    /// <summary>A page didn't look as expected: the portal or the DCP form has changed, or is down for maintenance.</summary>
    UnexpectedPage,

    /// <summary>RAI's form answered the save with an error message. <see cref="RaiException.PortalMessage"/> has its text.</summary>
    FormRejected,

    /// <summary>No connection, timeout, TLS or DNS problem.</summary>
    Network,
}

/// <summary>Something went wrong while talking to the RAI portal. The message is meant for the user.</summary>
public sealed class RaiException(RaiErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public RaiErrorKind Kind { get; } = kind;

    /// <summary>The error text RAI's page showed, if any.</summary>
    public string? PortalMessage { get; init; }

    /// <summary>
    /// Set when an upload failed after RAI had already saved part of the DCP. That partial DCP exists at
    /// RAI, and uploading again would create a second one.
    /// </summary>
    public UploadRecord? PartialUpload { get; init; }
}

/// <param name="LinesSaved">Lines RAI has confirmed so far.</param>
/// <param name="LinesTotal">Lines in the DCP.</param>
/// <param name="Message">What is happening, for the status bar.</param>
public sealed record UploadProgress(int LinesSaved, int LinesTotal, string Message);

/// <param name="Record">What to store in the DCP (<see cref="Dcp.Upload"/>).</param>
/// <param name="Verified">
/// True when the page RAI returned after the last save contained every uploaded title.
/// False doesn't mean failure, only that the check wasn't possible on that page.
/// </param>
/// <param name="Warnings">Things the user should look at in RAI's form, e.g. lines the check didn't find.</param>
public sealed record UploadResult(UploadRecord Record, bool Verified, IReadOnlyList<string> Warnings)
{
    public Uri Url => new(Record.Url);
}

/// <summary>
/// The RAI intranet DCP application, reached through RAI's SSL-VPN web portal. One instance holds one
/// portal session. All methods throw <see cref="RaiException"/> for anything that goes wrong, and
/// <see cref="OperationCanceledException"/> when cancelled.
/// </summary>
public interface IRaiClient
{
    bool IsLoggedIn { get; }

    /// <summary>User of the current session, or null.</summary>
    string? Username { get; }

    /// <summary>
    /// Logs in. If a session for other credentials exists it is replaced. The credentials are kept in
    /// memory so that an expired session can be renewed without asking again.
    /// </summary>
    Task LoginAsync(RaiCredentials credentials, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the DCP in RAI's form. Requires a previous <see cref="LoginAsync"/>; renews the session
    /// first when it has expired. The DCP must have passed validation for <c>ValidationPurpose.Upload</c>.
    /// </summary>
    Task<UploadResult> UploadAsync(Dcp dcp, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Everything an upload of this DCP would send, as readable text, without contacting RAI. For checking and for support.</summary>
    string DescribeUpload(Dcp dcp);

    /// <summary>Address of a page inside the portal, for the embedded browser.</summary>
    Uri GetPageUri(RaiPage page);

    /// <summary>The portal's address. Cookies from <see cref="GetSessionCookies"/> belong to it.</summary>
    Uri PortalUri { get; }

    /// <summary>The cookies of the current session, so an embedded browser can show portal pages without a second login.</summary>
    IReadOnlyList<Cookie> GetSessionCookies();

    /// <summary>
    /// Writes what was exchanged with the portal in this session (requests and pages, with passwords,
    /// cookies and session tokens removed) into a zip file in <paramref name="directory"/> and returns
    /// its path. This is what a user sends to the developers when an upload breaks.
    /// </summary>
    Task<string> SaveDiagnosticsAsync(string directory, CancellationToken cancellationToken = default);
}
