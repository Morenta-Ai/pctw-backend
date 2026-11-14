using Microsoft.AspNetCore.DataProtection;

namespace Rethread.Api.Domain;

/// <summary>Encrypts bank account numbers at rest. Only staff paying a transfer ever see one in full.</summary>
public sealed class BankAccountProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Rethread.BankAccountNumber.v1");

    public string Protect(string accountNumber) => _protector.Protect(accountNumber);

    public string Unprotect(string protectedNumber) => _protector.Unprotect(protectedNumber);

    public static string Last4(string accountNumber) =>
        accountNumber.Length <= 4 ? accountNumber : accountNumber[^4..];

    public static string Mask(string last4) => $"•••• {last4}";
}
