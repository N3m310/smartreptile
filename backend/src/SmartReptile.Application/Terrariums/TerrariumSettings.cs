namespace SmartReptile.Application.Terrariums;

/// <summary>
/// The terrarium settings the read use cases need. A plain record rather than <c>IOptions&lt;DefaultsOptions&gt;</c>
/// because the options class lives in Infrastructure: the composition root maps it across so Application never
/// depends on a configuration section (the same pattern as <c>ProvisioningSettings</c>).
/// </summary>
/// <param name="DefaultTimeZoneId">Zone assigned to a new terrarium when the request does not name one.</param>
/// <param name="SilentAfterIntervals">
/// Consecutive missed sampling intervals after which a device reads as offline (FR-07 / BR-07.2), so an
/// unplugged node stops claiming to be online before the M3 silence watchdog exists.
/// </param>
public sealed record TerrariumSettings(string DefaultTimeZoneId, int SilentAfterIntervals);
