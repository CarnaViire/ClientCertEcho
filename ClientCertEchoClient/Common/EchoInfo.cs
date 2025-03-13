namespace ClientCertEchoClient.Common;

record EchoInfo(
    CertInfo ClientCertificate,
    string SourceUserAgent,
    string SourceUserContext,
    string HttpClient,
    string HandlerId,
    int HandlerRequestNo);

record CertInfo(string Subject, string Thumbprint);