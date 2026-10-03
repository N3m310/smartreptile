using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Draws claim codes from the configured alphabet. <see cref="RandomNumberGenerator.GetInt32(int)"/> is used
/// rather than <c>% alphabet.Length</c> so the distribution stays uniform — modulo bias on a 32-symbol alphabet
/// would shave entropy off exactly the value that makes a 15-minute window safe.
/// </summary>
public sealed class ClaimCodeGenerator(IOptions<ProvisioningOptions> options) : IClaimCodeGenerator
{
    /// <inheritdoc />
    public string Generate()
    {
        var configured = options.Value;
        var alphabet = string.IsNullOrEmpty(configured.ClaimCodeAlphabet)
            ? ClaimCode.DefaultAlphabet
            : configured.ClaimCodeAlphabet;
        var length = configured.ClaimCodeLength > 0 ? configured.ClaimCodeLength : ClaimCode.DefaultLength;

        var code = new char[length];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(code);
    }
}
