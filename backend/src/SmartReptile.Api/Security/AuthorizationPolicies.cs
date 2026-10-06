namespace SmartReptile.Api.Security;

/// <summary>
/// Authorisation policy names from the role matrix in §02-design/06 §3. An endpoint names a policy, never the
/// role string and never the rule, so changing the matrix is a change in one file.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Owner only: terrarium CRUD, device claim/rebind/revoke/rotate, thresholds, calibration, roles.</summary>
    public const string Owner = "Owner";

    /// <summary>Owner or Technician: acknowledging, resolving and silencing alerts.</summary>
    public const string Technician = "Technician";
}
