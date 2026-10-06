using System.Security.Cryptography;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Draws recovery codes from the domain's alphabet. <see cref="RandomNumberGenerator.GetInt32(int)"/> is used
/// rather than <c>% alphabet.Length</c> so the distribution stays uniform: modulo bias would shave entropy off the
/// one thing standing between a forgotten password and someone else's account.
/// </summary>
public sealed class RecoveryCodeGenerator : IRecoveryCodeGenerator
{
    /// <inheritdoc />
    public string Generate()
    {
        var code = new char[RecoveryCode.DefaultLength];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = RecoveryCode.DefaultAlphabet[RandomNumberGenerator.GetInt32(RecoveryCode.DefaultAlphabet.Length)];
        }

        return new string(code);
    }
}
