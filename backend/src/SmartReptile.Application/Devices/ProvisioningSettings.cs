using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Devices;

/// <summary>
/// The onboarding settings the use cases need. A plain record rather than
/// <c>IOptions&lt;ProvisioningOptions&gt;</c> because the options class lives in Infrastructure: the composition
/// root maps it across so Application never depends on a configuration section.
/// </summary>
/// <param name="ClaimCodeAlphabet">Alphabet codes are drawn from (§07-appendices/03 §2.1).</param>
/// <param name="ClaimCodeLength">Code length.</param>
/// <param name="ClaimCodeMinutes">Claim-code lifetime in minutes (BR-04.1).</param>
/// <param name="Onboarding">Self-registration anti-abuse limits (§02-design/06 §5).</param>
public sealed record ProvisioningSettings(
    string ClaimCodeAlphabet,
    int ClaimCodeLength,
    int ClaimCodeMinutes,
    OnboardingLimits Onboarding);
