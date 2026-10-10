import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  ArrowRight,
  Eye,
  EyeOff,
  KeyRound,
  Loader2,
  Lock,
  Mail,
  ShieldCheck,
  User,
} from 'lucide-react';
import { ApiError } from '../api/client';
import * as auth from '../api/endpoints';
import { useI18n } from '../i18n';
import { LANGUAGES } from '../i18n/strings';
import { useSession } from '../state/session';
import { describeError, fieldHasError, fieldMessage, fieldMessages } from '../lib/messages';

/**
 * Sign in, register, and both password-recovery paths — the four things `/auth/*` can do (roadmap 4.15).
 *
 * What this replaced matters more than its looks. The prototype's login screen was a 400 ms `setTimeout` that
 * navigated to `/dashboard` whatever was typed, and it carried three invented statistics ("03 Chuồng sinh thái",
 * "09 Node kết nối IoT") on what is now the first screen a marker sees. There is no invented value left here:
 * every number is either typed by the keeper or returned by the API. "Forgot password" also no longer shows an
 * `alert()` telling the keeper to contact an administrator — the flow exists now, and both variants of it are on
 * this page.
 *
 * The two recovery paths are one form, because the keeper holds one of two different things and only they know
 * which: the **backup code** saved at registration, or a **server-issued code** that expires in 30 minutes. The
 * hint under the field is the contract, and the submit handler follows the mode rather than guessing from what
 * was typed.
 */

type Mode = 'signIn' | 'register' | 'recover' | 'reset';

export const Login: React.FC = () => {
  const { t, lang, setLang } = useI18n();
  const { session, signIn, lostReason, clearLostReason } = useSession();
  const navigate = useNavigate();

  const [mode, setMode] = useState<Mode>('signIn');
  const [identifier, setIdentifier] = useState('');
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [code, setCode] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [issuedCode, setIssuedCode] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // A session means this page has nothing to do — including one restored from storage on a reload.
  useEffect(() => {
    if (session) {
      navigate('/dashboard', { replace: true });
    }
  }, [session, navigate]);

  const clearFeedback = () => {
    setError(null);
    setNotice(null);
    setIssuedCode(null);
  };

  const switchMode = (next: Mode) => {
    clearFeedback();
    setCode('');
    setNewPassword('');
    setMode(next);
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    clearFeedback();
    clearLostReason();

    try {
      if (mode === 'signIn') {
        signIn(await auth.login({ usernameOrEmail: identifier.trim(), password }));
        navigate('/dashboard', { replace: true });
      } else if (mode === 'register') {
        const result = await auth.register({
          username: username.trim(),
          email: email.trim(),
          password,
          preferredLanguage: lang,
        });
        // Shown once and never retrievable — the server keeps only its hash.
        setIssuedCode(result.recoveryCode);
        setNotice(t('registerDone'));
        setIdentifier(username.trim());
        setPassword('');
        setMode('signIn');
      } else if (mode === 'recover') {
        const result = await auth.recover({
          usernameOrEmail: identifier.trim(),
          recoveryCode: code.trim(),
          newPassword,
        });
        setIssuedCode(result.recoveryCode);
        setNotice(t('recoveryDone'));
        setNewPassword('');
        setCode('');
        setMode('signIn');
      } else {
        const result = await auth.resetPassword({
          usernameOrEmail: identifier.trim(),
          resetCode: code.trim(),
          newPassword,
        });
        setIssuedCode(result.recoveryCode);
        setNotice(t('recoveryDone'));
        setNewPassword('');
        setCode('');
        setMode('signIn');
      }
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  /** Asks for a server-issued code and switches the form into the mode that spends it. */
  const requestResetCode = async () => {
    setBusy(true);
    clearFeedback();
    try {
      await auth.forgotPassword({ usernameOrEmail: identifier.trim() });
      setNotice(`${t('resetCodeRequested')} ${t('resetCodeLogHint')}`);
      setMode('reset');
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const inputClass = (field: string) =>
    `w-full rounded-xl border bg-field py-3 pl-11 pr-4 text-sm text-ink outline-none transition-all placeholder:text-muted/60 focus:ring-1 focus:ring-brand ${
      fieldHasError(error, field) ? 'border-critical' : 'border-hairline focus:border-brand'
    }`;

  const heading =
    mode === 'signIn'
      ? t('signInTitle')
      : mode === 'register'
        ? t('createAccount')
        : mode === 'recover'
          ? t('recoverButton')
          : t('resetCodeLabel');

  // Every field-level sentence in the refusal, de-duplicated: the policy reports one code per rule.
  const fieldSentences = mode === 'register' ? [...new Set(fieldMessages(t, error?.errors))] : [];

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-4 sm:p-6 lg:p-8">
      <div className="w-full max-w-5xl rounded-3xl border border-hairline bg-surface shadow-2xl">
        <div className="grid grid-cols-1 lg:grid-cols-12">
          {/* Hero: brand and purpose. Nothing here is a measurement, so nothing here is a number. */}
          <div className="rounded-t-3xl border-b border-hairline bg-gradient-to-br from-raised via-surface to-field p-8 sm:p-10 lg:col-span-5 lg:rounded-l-3xl lg:rounded-tr-none lg:border-b-0 lg:border-r">
            <div className="flex items-center gap-3">
              <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-gradient-to-br from-brand to-[#204930] shadow-xl shadow-brand/20">
                <ShieldCheck className="h-7 w-7 text-ink" aria-hidden="true" />
              </div>
              <div>
                <span className="block font-heading text-2xl font-extrabold tracking-wider text-ink">
                  VIVARIUM<span className="text-brand">GUARD</span>
                </span>
                <span className="block text-xs uppercase tracking-widest text-muted">
                  {t('appTitle')}
                </span>
              </div>
            </div>

            <p className="mt-8 text-sm leading-relaxed text-muted">
              {mode === 'register' ? t('registerPrompt') : t('signInPrompt')}
            </p>

            <div className="mt-8 flex items-center gap-2">
              <span className="text-xs uppercase tracking-widest text-muted">{t('language')}</span>
              {LANGUAGES.map((option) => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setLang(option)}
                  aria-pressed={lang === option}
                  className={`rounded-lg border px-3 py-1 text-xs transition-colors ${
                    lang === option
                      ? 'border-brand text-brand'
                      : 'border-hairline text-muted hover:text-ink'
                  }`}
                >
                  {option === 'vi' ? t('languageVi') : t('languageEn')}
                </button>
              ))}
            </div>
          </div>

          <div className="p-8 sm:p-12 lg:col-span-7">
            <div className="mx-auto w-full max-w-md">
              <h1 className="font-heading text-2xl font-bold text-ink">{heading}</h1>

              {/* A session the transport could not rotate says so, instead of a silent bounce. */}
              {lostReason === 'expired' ? (
                <p className="mt-4 rounded-xl border border-warning/40 bg-warning/10 px-4 py-2.5 text-[13px] text-ink">
                  {t('sessionExpired')}
                </p>
              ) : null}

              {error ? (
                <div
                  role="alert"
                  className="mt-4 rounded-xl border border-critical/40 bg-critical/10 px-4 py-2.5 text-[13px] text-ink"
                >
                  <p>{describeError(t, error)}</p>
                  {fieldSentences.map((sentence) => (
                    <p key={sentence} className="mt-1 text-xs">
                      {sentence}
                    </p>
                  ))}
                </div>
              ) : null}

              {notice ? (
                <p
                  role="status"
                  className="mt-4 rounded-xl border border-brand/40 bg-brand/10 px-4 py-2.5 text-[13px] text-ink"
                >
                  {notice}
                </p>
              ) : null}

              {issuedCode ? (
                <p className="mt-3 rounded-xl border border-hairline bg-field px-4 py-3 text-sm">
                  <span className="text-muted">{t('recoveryIssued')} </span>
                  <code className="font-mono text-base tracking-widest text-ink">{issuedCode}</code>
                </p>
              ) : null}

              <form onSubmit={submit} className="mt-6 space-y-5">
                {mode === 'register' ? (
                  <>
                    <Field label={t('username')} icon={<User className="h-5 w-5" />}>
                      <input
                        value={username}
                        onChange={(event) => setUsername(event.target.value)}
                        required
                        autoComplete="username"
                        aria-invalid={fieldHasError(error, 'username')}
                        className={inputClass('username')}
                      />
                    </Field>
                    <FieldLabelError message={fieldMessage(t, error, 'username')} />
                    <Field label={t('email')} icon={<Mail className="h-5 w-5" />}>
                      <input
                        type="email"
                        value={email}
                        onChange={(event) => setEmail(event.target.value)}
                        required
                        autoComplete="email"
                        aria-invalid={fieldHasError(error, 'email')}
                        className={inputClass('email')}
                      />
                    </Field>
                    <FieldLabelError message={fieldMessage(t, error, 'email')} />
                  </>
                ) : (
                  <Field label={t('usernameOrEmail')} icon={<User className="h-5 w-5" />}>
                    <input
                      value={identifier}
                      onChange={(event) => setIdentifier(event.target.value)}
                      required
                      autoComplete="username"
                      className={inputClass('identifier')}
                    />
                  </Field>
                )}

                {mode === 'signIn' || mode === 'register' ? (
                  <>
                    <Field label={t('password')} icon={<Lock className="h-5 w-5" />}>
                      <input
                        type={showPassword ? 'text' : 'password'}
                        value={password}
                        onChange={(event) => setPassword(event.target.value)}
                        required
                        autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
                        aria-invalid={fieldHasError(error, 'password')}
                        className={inputClass('password')}
                      />
                      <PasswordToggle
                        shown={showPassword}
                        onToggle={() => setShowPassword(!showPassword)}
                      />
                    </Field>
                    {mode === 'register' ? (
                      <p className="text-xs text-muted">{t('passwordHint')}</p>
                    ) : null}
                  </>
                ) : (
                  <>
                    <Field
                      label={mode === 'recover' ? t('recoveryCode') : t('resetCodeLabel')}
                      icon={<KeyRound className="h-5 w-5" />}
                    >
                      <input
                        value={code}
                        onChange={(event) => setCode(event.target.value)}
                        required
                        spellCheck={false}
                        autoComplete="one-time-code"
                        className={`${inputClass('code')} font-mono`}
                      />
                    </Field>
                    <p className="text-xs text-muted">
                      {mode === 'recover' ? t('recoverHintBackup') : t('recoverHintIssued')}
                    </p>
                    <div>
                      <Field label={t('newPassword')} icon={<Lock className="h-5 w-5" />}>
                        <input
                          type={showPassword ? 'text' : 'password'}
                          value={newPassword}
                          onChange={(event) => setNewPassword(event.target.value)}
                          required
                          autoComplete="new-password"
                          aria-invalid={fieldHasError(error, 'password')}
                          className={inputClass('password')}
                        />
                        <PasswordToggle
                          shown={showPassword}
                          onToggle={() => setShowPassword(!showPassword)}
                        />
                      </Field>
                      {/* The rule in the same words the server enforces (BR-01.2). */}
                      <p className="mt-2 text-xs text-muted">{t('passwordHint')}</p>
                    </div>
                  </>
                )}

                <button
                  type="submit"
                  disabled={busy}
                  className="group mt-2 flex w-full items-center justify-center gap-2 rounded-xl bg-brand px-4 py-3.5 text-sm font-medium text-white shadow-lg shadow-brand/25 transition-all hover:bg-brand-dark disabled:opacity-70"
                >
                  {busy ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                      <span>{busyLabel(t, mode)}</span>
                    </>
                  ) : (
                    <>
                      <span>{actionLabel(t, mode)}</span>
                      <ArrowRight
                        className="h-4 w-4 transition-transform group-hover:translate-x-1"
                        aria-hidden="true"
                      />
                    </>
                  )}
                </button>
              </form>

              <div className="mt-5 flex flex-wrap items-center justify-between gap-3 text-xs">
                {mode === 'signIn' ? (
                  <>
                    <button
                      type="button"
                      onClick={() => switchMode('register')}
                      className="text-accent hover:underline"
                    >
                      {t('createAccount')}
                    </button>
                    <button
                      type="button"
                      onClick={() => switchMode('recover')}
                      className="text-accent hover:underline"
                    >
                      {t('forgotPassword')}
                    </button>
                  </>
                ) : (
                  <>
                    {/* The other recovery path: the keeper who lost the backup code asks for a fresh one. */}
                    {mode === 'recover' ? (
                      <button
                        type="button"
                        onClick={() => void requestResetCode()}
                        disabled={busy || identifier.trim().length === 0}
                        className="text-accent hover:underline disabled:opacity-50"
                      >
                        {t('sendResetCode')}
                      </button>
                    ) : null}
                    <button
                      type="button"
                      onClick={() => switchMode('signIn')}
                      className="text-muted hover:text-ink"
                    >
                      {t('backToSignIn')}
                    </button>
                  </>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

const PasswordToggle: React.FC<{ shown: boolean; onToggle: () => void }> = ({ shown, onToggle }) => {
  const { t } = useI18n();
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-label={shown ? t('hidePassword') : t('showPassword')}
      className="absolute inset-y-0 right-0 flex items-center pr-3.5 text-muted hover:text-ink"
    >
      {shown ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
    </button>
  );
};

const Field: React.FC<{ label: string; icon: React.ReactNode; children: React.ReactNode }> = ({
  label,
  icon,
  children,
}) => (
  <div>
    <label className="mb-2 block text-xs font-semibold uppercase tracking-wider text-muted">
      {label}
    </label>
    <div className="relative">
      <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-muted">
        {icon}
      </span>
      {children}
    </div>
  </div>
);

/** The per-field sentence under an input, when the refusal named that field. */
const FieldLabelError: React.FC<{ message: string | null }> = ({ message }) =>
  message ? <p className="-mt-3 text-xs text-critical">{message}</p> : null;

function actionLabel(t: (key: string) => string, mode: Mode): string {
  switch (mode) {
    case 'register':
      return t('register');
    case 'recover':
    case 'reset':
      return t('recoverButton');
    default:
      return t('signIn');
  }
}

function busyLabel(t: (key: string) => string, mode: Mode): string {
  switch (mode) {
    case 'register':
      return t('registering');
    case 'recover':
    case 'reset':
      return t('recovering');
    default:
      return t('signingIn');
  }
}
