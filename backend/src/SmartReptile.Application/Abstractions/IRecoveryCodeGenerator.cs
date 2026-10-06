namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Issues account recovery codes. A port rather than a static helper, for the same two reasons as
/// <see cref="IClaimCodeGenerator"/>: the alphabet and length are not baked into the use case, and a test can
/// name the code it expects instead of accepting whatever randomness produced.
/// </summary>
public interface IRecoveryCodeGenerator
{
    /// <summary>A fresh code drawn from the unambiguous alphabet (§02-design/06 §2).</summary>
    string Generate();
}
