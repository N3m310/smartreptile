/*
 * Problem code → sentence. One table, so a refusal reads the same wherever it arrives.
 *
 * Roadmap 4.15's acceptance is "each failure the API can return is rendered from its stable `code`, not a
 * generic message". That is this file, and its two failure modes are both deliberate:
 *
 *   - a code with no entry falls back to `errorRequestFailed` **with the code shown**, which is a visible gap
 *     rather than a wrong sentence;
 *   - a `401` with no code is not treated as "wrong password" — the transport has already tried to rotate, so by
 *     the time it reaches here the session is gone and the sentence says so.
 *
 * `fieldMessages` is the other half: `registration_invalid` carries `errors[]` with a code per field, and a form
 * that answered "the details were refused" for a taken email would be a worse form than the one this replaces
 * (BUG-05, ADR-020).
 */

import { ApiError } from '../api/client';
import type { FieldViolation } from '../api/types';
import type { TFunc } from './format';

const CODE_KEY: Record<string, string> = {
  invalid_credentials: 'invalidCredentials',
  unauthenticated: 'unauthenticated',
  token_invalid: 'sessionExpired',
  token_reused: 'sessionExpired',
  account_locked: 'accountLocked',
  ip_blocked: 'accountLocked',
  rate_limited: 'rateLimited',
  identifier_unknown: 'identifierUnknown',
  invalid_recovery_code: 'recoveryFailed',
  invalid_reset_code: 'recoveryFailed',
  password_policy_violation: 'passwordPolicyViolation',
  registration_invalid: 'registrationInvalid',
  registration_conflict: 'registrationInvalid',
  validation_failed: 'errorValidationFailed',
  not_found: 'errorNotFound',
  device_not_bound: 'errorDeviceNotBound',
  range_too_large: 'errorRangeTooLarge',
  metric_invalid: 'errorMetricInvalid',
  invalid_range: 'errorInvalidRange',
  species_profile_not_found: 'errorProfileNotFound',
  // The catalogue's own refusals (FR-10). Top-level codes rather than field names, because their answers are "a
  // built-in cannot be edited" and "a terrarium is using it" — neither is a message about one input.
  builtin_immutable: 'builtinImmutable',
  profile_in_use: 'profileInUse',
  profile_name_taken: 'profileNameTaken',
  // The picture refusals arrive both ways — as a top-level code and as `errors[].field = "file"` — so they are in
  // this table and in the field table below. A form that answered "the request was refused" for a 3 MB photograph
  // would tell the keeper nothing about what to do differently.
  image_required: 'imageRequired',
  image_too_large: 'imageTooLarge',
  image_type_unsupported: 'imageTypeUnsupported',
  image_invalid: 'imageInvalid',
  image_not_set: 'imageNotSet',
  // The administrator's refusals (FR-18). Unlike the authentication group these are meant to be read by the
  // person who caused them: "that is the last administrator" and "you cannot do that to your own account" are
  // rules an administrator has to know, so each gets its own sentence rather than one opaque failure.
  user_not_found: 'adminAccountNotFound',
  self_change_refused: 'adminErrorSelfChange',
  last_administrator_refused: 'adminErrorLastAdministrator',
  account_in_use: 'adminErrorAccountInUse',
  account_invalid: 'adminErrorAccountInvalid',
  // The reset route validates the new password with the same policy registration uses, so its refusal is the
  // policy's own sentence rather than a second one that could disagree with it.
  password_invalid: 'passwordPolicyViolation',
  quiet_hours_incomplete: 'adminFormQuietHoursHint',
  quiet_hours_invalid: 'adminFormQuietHoursFormat',
  severity_invalid: 'adminFormSeverityValues',
  role_invalid: 'adminErrorRoleInvalid',
  role_not_found: 'adminErrorRoleNotFound',
  role_in_use: 'adminErrorRoleInUse',
  role_code_taken: 'adminRoleCodeTaken',
  role_code_invalid: 'adminErrorRoleInvalid',
  role_name_invalid: 'adminErrorRoleInvalid',
  role_description_invalid: 'adminErrorRoleInvalid',
  system_role_immutable: 'adminErrorSystemRole',
  permission_unknown: 'adminErrorPermissionUnknown',
  // The two list filters cannot be triggered by this client — its options are the two families and the three
  // states — so a refusal is a validation sentence rather than a vocabulary of its own.
  family_invalid: 'errorValidationFailed',
  state_invalid: 'errorValidationFailed',
  maintenance: 'maintenanceNotice',
  timeout: 'errorServiceUnavailable',
  network_unreachable: 'errorBackendUnreachable',
};

/** The password rule codes, which the policy reports one per rule so a form can name the one that failed. */
const PASSWORD_RULE_KEY: Record<string, string> = {
  password_required: 'passwordRequired',
  password_too_short: 'passwordTooShort',
  password_too_long: 'passwordTooLong',
  password_letter_required: 'passwordNeedsLetter',
  password_digit_required: 'passwordNeedsDigit',
  password_uppercase_required: 'passwordNeedsUppercase',
  password_special_required: 'passwordNeedsSpecial',
  password_too_common: 'passwordTooCommon',
};

/** Field-level codes from `register`/`POST /terrariums`, in the order a form should show them. */
const FIELD_KEY: Record<string, string> = {
  ...PASSWORD_RULE_KEY,
  username_taken: 'usernameTaken',
  email_taken: 'emailTaken',
  username_too_short: 'usernameTooShort',
  username_invalid: 'usernameInvalid',
  email_invalid: 'emailInvalid',
  terrarium_name_required: 'terrariumNameRequired',
  terrarium_name_too_long: 'terrariumNameTooLong',
  terrarium_location_too_long: 'terrariumLocationTooLong',
  terrarium_description_too_long: 'terrariumDescriptionTooLong',
  terrarium_timezone_invalid: 'terrariumTimeZoneInvalid',
  species_profile_not_found: 'errorProfileNotFound',
  // The band rules of `03-implementation/06` §2, one code per rule so the editor marks the input that is wrong
  // rather than showing one opaque refusal (BR-10.2).
  threshold_target_required: 'thresholdTargetRequired',
  threshold_ordering_invalid: 'thresholdOrderingInvalid',
  threshold_critical_invalid: 'thresholdCriticalInvalid',
  threshold_dwell_invalid: 'thresholdDwellInvalid',
  threshold_recovery_margin_invalid: 'thresholdMarginInvalid',
  threshold_implausible_band: 'thresholdImplausibleBand',
  threshold_duplicate: 'thresholdDuplicate',
  // The catalogue's field rules (`SpeciesProfileService.ParseFields`/`ParseBands`), addressed by field name or by
  // position, so the editor marks the input that is wrong rather than showing one opaque refusal.
  profile_name_required: 'profileNameRequired',
  profile_name_too_long: 'profileNameTooLong',
  profile_name_taken: 'profileNameTaken',
  // The picture rules, all addressed to the `file` part of the multipart body.
  image_required: 'imageRequired',
  image_too_large: 'imageTooLarge',
  image_type_unsupported: 'imageTypeUnsupported',
  image_invalid: 'imageInvalid',
  climate_zone_invalid: 'climateZoneInvalid',
  scientific_name_too_long: 'scientificNameTooLong',
  profile_notes_too_long: 'profileNotesTooLong',
  photoperiod_invalid: 'photoperiodInvalid',
  lights_on_invalid: 'lightsOnInvalid',
  light_threshold_invalid: 'lightThresholdInvalid',
  min_light_hours_invalid: 'minLightHoursInvalid',
  profile_bands_required: 'profileBandsRequired',
  threshold_phase_invalid: 'thresholdPhaseInvalid',
  source_ref_too_long: 'sourceRefTooLong',
  // The account and role fields (`UserAdminService`, `RoleAdminService`), addressed by field name so the editor
  // marks the input that is wrong. The three role-shape codes share one sentence, and the violation's own field
  // name is what puts it under the right input.
  quiet_hours_incomplete: 'adminFormQuietHoursHint',
  quiet_hours_invalid: 'adminFormQuietHoursFormat',
  severity_invalid: 'adminFormSeverityValues',
  role_invalid: 'adminErrorRoleInvalid',
  role_code_taken: 'adminRoleCodeTaken',
  role_code_invalid: 'adminErrorRoleInvalid',
  role_name_invalid: 'adminErrorRoleInvalid',
  role_description_invalid: 'adminErrorRoleInvalid',
  permission_unknown: 'adminErrorPermissionUnknown',
};

/** The sentence for a failed call. `apiBaseUrl` and the code are shown when the table has no entry. */
export function describeError(t: TFunc, error: ApiError | null): string {
  if (!error) {
    return '';
  }
  if (error.isNotBuilt) {
    return t('errorNotBuilt');
  }
  const key = CODE_KEY[error.code];
  if (key) {
    return t(key);
  }
  if (error.isNetworkFailure) {
    return t('errorBackendUnreachable');
  }
  return `${t('errorRequestFailed')} (${error.code})`;
}

/**
 * The sentence for a failed change-password call.
 *
 * `/auth/change-password` answers a wrong current password with the same opaque `401 invalid_credentials` a failed
 * sign-in gets (`07-appendices/03` §4.1) — "this credential is wrong" is one condition with one code — but the
 * sentence that code maps to names a *username*, and this form has no username field to be wrong: the only
 * credential it can send is the one it is asking about. The deck's `wrongCurrentPassword` is therefore the honest
 * reading of that code here. Every other refusal — the policy violation, the lockout, an unreachable server — is
 * the sentence its own code already has.
 */
export function describeChangePasswordError(t: TFunc, error: ApiError | null): string {
  return error?.code === 'invalid_credentials' ? t('wrongCurrentPassword') : describeError(t, error);
}

/**
 * The rule-by-rule sentences behind a refusal, e.g. every password rule a new password broke. Empty when the
 * refusal carried no field detail — which is not a failure of this function, it is an answer with no detail.
 */
export function fieldMessages(t: TFunc, violations: FieldViolation[] | undefined): string[] {
  return (violations ?? [])
    .map((violation) => FIELD_KEY[violation.code] ?? PASSWORD_RULE_KEY[violation.code])
    .filter((key): key is string => key !== undefined)
    .map((key) => t(key));
}

/** The message for one field of a form, or null when that field was not the problem. */
export function fieldMessage(t: TFunc, error: ApiError | null, field: string): string | null {
  if (!error || error.code === 'password_policy_violation') {
    return null;
  }
  const messages = fieldMessages(
    t,
    error.errors.filter((violation) => violation.field === field)
  );
  return messages.length > 0 ? messages[0] : null;
}

/** True when the failure was this field's fault — used to outline the offending input. */
export function fieldHasError(error: ApiError | null, field: string): boolean {
  return Boolean(error?.errors?.some((violation) => violation.field === field));
}
