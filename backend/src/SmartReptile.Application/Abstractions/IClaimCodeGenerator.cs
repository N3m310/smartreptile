namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Generates onboarding codes. A port rather than a static helper so the alphabet and length stay configurable
/// and so tests can issue a predictable code without caring about randomness.
/// </summary>
public interface IClaimCodeGenerator
{
    /// <summary>A fresh code drawn from the unambiguous alphabet (§07-appendices/03 §2.1).</summary>
    string Generate();
}
