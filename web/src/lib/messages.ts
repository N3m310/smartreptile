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
