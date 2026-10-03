namespace SmartReptile.Application.Identity;

/// <summary>
/// The session settings the authentication use cases need. A plain record rather than
/// <c>IOptions&lt;JwtOptions&gt;</c> because <c>JwtOptions</c> lives in Infrastructure: the composition root
/// maps it across so Application never depends on a configuration section.
/// </summary>
/// <param name="RefreshTokenDays">Refresh-token lifetime (BR-01.3).</param>
public sealed record AuthSettings(int RefreshTokenDays = 30);
