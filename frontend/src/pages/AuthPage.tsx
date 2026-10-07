import { t, LanguageSelector } from '../i18n';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useMutation } from '@tanstack/react-query';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../Auth';
import { ErrorNotice, FieldError } from '../components';
import type { Session } from '../types';

const loginSchema = z.object({ email: z.email(t("Enter a valid email address.")).max(254), password: z.string().min(1, t("Enter your password.")).max(128) });
const registerSchema = loginSchema.extend({ name: z.string().trim().min(1, t("Enter your name.")).max(100),
  password: z.string().min(12, t("Use at least 12 characters.")).max(128), confirmPassword: z.string() })
  .refine(value => value.password === value.confirmPassword, { path: ['confirmPassword'], message: t("Passwords do not match.") });
type LoginInput = z.infer<typeof loginSchema>;
type RegisterInput = z.infer<typeof registerSchema>;

export function AuthPage({ registerMode = false }: { registerMode?: boolean }) {
  const auth = useAuth();
  if (auth.session) return <Navigate to="/products" replace />;
  return <main className="auth-shell">
    <aside className="auth-aside">
      <Link to="/auth/login" className="brand text-white"><span className="brand-icon">{t("C")}</span> {t("Catalog")}<span className="brand-dot">.</span></Link>
      <div><p className="eyebrow text-emerald-200">{t("YOUR PRODUCT WORKSPACE")}</p>
        <h1 className="mt-5 text-5xl font-semibold leading-tight">{t("A little order.")}<br />{t("A lot of clarity.")}</h1>
        <p className="mt-6 max-w-sm leading-7 text-emerald-100/75">{t("Keep your products in one place. Create, update, and manage your catalog with a simple workspace built for focus.")}</p>
      </div>
      <p className="text-sm text-emerald-100/60">{t("Simple tools. Thoughtful details.")}</p>
    </aside>
    <section className="auth-content">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex justify-end"><LanguageSelector /></div>
        <Link to="/auth/login" className="brand mb-10 lg:hidden">{t("Catalog.")}</Link>
        <p className="eyebrow">{t("LET’S GET STARTED")}</p>
        <h2 className="mt-3 text-3xl font-semibold tracking-tight">{registerMode ? t("Create your account") : t("Welcome back")}</h2>
        <p className="mb-8 mt-3 text-slate-500">{registerMode ? t("Your catalog starts here.") : t("Sign in to your product workspace.")}</p>
        {registerMode ? <RegisterForm /> : <LoginForm />}
        <p className="mt-7 text-sm text-slate-500">{registerMode ? t("Already have an account?") : t("New to Catalog?")}{' '}
          <Link className="text-link" to={registerMode ? '/auth/login' : '/auth/register'}>{registerMode ? t("Sign in") : t("Create an account")}</Link>
        </p>
      </div>
    </section>
  </main>;
}

function LoginForm() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { register, handleSubmit, formState: { errors } } = useForm<LoginInput>({ resolver: zodResolver(loginSchema) });
  const mutation = useMutation({ mutationFn: (value: LoginInput) => api<Session>('/auth/login', { method: 'POST', body: JSON.stringify(value) }),
    onSuccess: session => { auth.login(session); navigate('/products', { replace: true }); } });
  return <form className="space-y-5" noValidate onSubmit={handleSubmit(value => mutation.mutate(value))}>
    {params.has('registered') && <p className="notice" role="status">{t("Account created. Sign in to continue.")}</p>}
    <ErrorNotice error={mutation.error} />
    <label className="field">{t("Email address")}<input type="email" autoComplete="email" aria-label={t('Email address')} aria-describedby={errors.email ? 'login-email-error' : undefined} {...register('email')} aria-invalid={!!errors.email} /><FieldError id="login-email-error" message={errors.email?.message} /></label>
    <label className="field">{t("Password")}<input type="password" autoComplete="current-password" aria-label={t('Password')} aria-describedby={errors.password ? 'login-password-error' : undefined} {...register('password')} aria-invalid={!!errors.password} /><FieldError id="login-password-error" message={errors.password?.message} /></label>
    <button className="button w-full" disabled={mutation.isPending}>{mutation.isPending ? t("Signing in…") : t("Sign in")}</button>
  </form>;
}

function RegisterForm() {
  const navigate = useNavigate();
  const { register, handleSubmit, formState: { errors } } = useForm<RegisterInput>({ resolver: zodResolver(registerSchema) });
  const mutation = useMutation({ mutationFn: ({ confirmPassword: _, ...value }: RegisterInput) => api('/auth/register', { method: 'POST', body: JSON.stringify(value) }),
    onSuccess: () => navigate('/auth/login?registered=1', { replace: true }) });
  return <form className="space-y-5" noValidate onSubmit={handleSubmit(value => mutation.mutate(value))}>
    <ErrorNotice error={mutation.error} />
    <label className="field">{t("Full name")}<input autoComplete="name" {...register('name')} aria-invalid={!!errors.name} /><FieldError message={errors.name?.message} /></label>
    <label className="field">{t("Email address")}<input type="email" autoComplete="email" {...register('email')} aria-invalid={!!errors.email} /><FieldError message={errors.email?.message} /></label>
    <div className="field"><label htmlFor="register-password">{t("Password")}</label><input id="register-password" type="password" autoComplete="new-password" {...register('password')} aria-describedby="password-hint" aria-invalid={!!errors.password} /><span id="password-hint" className="text-xs font-normal text-slate-500">{t("At least 12 characters.")}</span><FieldError message={errors.password?.message} /></div>
    <label className="field">{t("Confirm password")}<input type="password" autoComplete="new-password" {...register('confirmPassword')} aria-invalid={!!errors.confirmPassword} /><FieldError message={errors.confirmPassword?.message} /></label>
    <button className="button w-full" disabled={mutation.isPending}>{mutation.isPending ? t("Creating account…") : t("Create account")}</button>
  </form>;
}
