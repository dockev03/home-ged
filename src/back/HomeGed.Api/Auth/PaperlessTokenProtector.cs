using Microsoft.AspNetCore.DataProtection;

namespace HomeGed.Api.Auth;

/// <summary>Chiffrement au repos du token Paperless stocké dans la session.</summary>
public sealed class PaperlessTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("HomeGed.Sessions.PaperlessToken.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}
