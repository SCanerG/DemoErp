import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { BusinessStatus, FieldError, Loading, ErrorNotice } from './components';
import { LanguageSelector, setLanguage, t } from './i18n';
import { allowed, RequirePermission } from './permissions';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './Auth';
import { AuthPage } from './pages/AuthPage';
import { StatusProgress } from './StatusProgress';
import { SuccessNotice, announceSuccess } from './SuccessNotice';
import { act } from '@testing-library/react';

afterEach(() => setLanguage('en'));
describe('shared UI behavior', () => {
  it.each(['Draft', 'Released', 'InProgress', 'Completed', 'Cancelled'])('communicates %s in text', status => {
    render(<BusinessStatus status={status} />);
    expect(screen.getByText(status === 'InProgress' ? 'In progress' : status)).toBeVisible();
  });
  it('announces loading without presenting invented metrics', () => {
    render(<Loading />);
    expect(screen.getByRole('status')).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Loading workspace…');
  });
  it('displays field errors and omits absent errors', () => {
    const { rerender } = render(<FieldError message="Enter a product name." id="error" />);
    expect(screen.getByRole('alert')).toHaveAttribute('id', 'error');
    rerender(<FieldError />);
    expect(screen.queryByRole('alert')).toBeNull();
  });
  it('switches language and updates document language and labels', () => {
    render(<LanguageSelector />);
    fireEvent.click(screen.getByRole('button', { name: 'TR' }));
    expect(document.documentElement.lang).toBe('tr');
    expect(t('Created at')).toBe('Oluşturulma zamanı');
    expect(screen.getByRole('button', { name: 'TR' })).toHaveAttribute('aria-pressed', 'true');
  });
  it('uses a safe Turkish fallback for untranslated validation', () => {
    setLanguage('tr'); render(<FieldError message="Internal schema validation string" />);
    expect(screen.getByRole('alert')).not.toHaveTextContent('Internal schema');
  });
  it('omits empty API error notices', () => {
    render(<ErrorNotice error={null} />); expect(screen.queryByRole('alert')).toBeNull();
  });
  it('denies anonymous protected content', () => {
    sessionStorage.removeItem('catalog.session');
    render(<QueryClientProvider client={new QueryClient()}><MemoryRouter><AuthProvider><RequirePermission permission="users"><p>Private data</p></RequirePermission></AuthProvider></MemoryRouter></QueryClientProvider>);
    expect(screen.queryByText('Private data')).toBeNull();
    expect(screen.getByRole('heading', { name: 'Access Denied' })).toBeVisible();
  });
  it('preserves role boundaries', () => {
    expect(allowed({ role: 'Viewer' }, 'write')).toBe(false);
    expect(allowed({ role: 'Manager' }, 'users')).toBe(false);
    expect(allowed({ role: 'Admin' }, 'users')).toBe(true);
  });
  it('validates login inputs before making a request', async () => {
    render(<QueryClientProvider client={new QueryClient()}><MemoryRouter><AuthProvider><AuthPage /></AuthProvider></MemoryRouter></QueryClientProvider>);
    const email = screen.getByLabelText('Email address');
    expect(email).toHaveAttribute('autocomplete', 'email');
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Enter a valid email address.')).toBeVisible();
    expect(email).toHaveAttribute('aria-invalid', 'true');
  });
  it('marks only the actual current workflow step', () => {
    render(<StatusProgress status="Confirmed" steps={['Pending', 'Confirmed', 'Completed']} />);
    expect(screen.getByText('Confirmed').closest('li')).toHaveAttribute('aria-current', 'step');
    expect(screen.getByText('Completed').closest('li')).not.toHaveAttribute('aria-current');
  });
  it('shows cancellation without inventing completed stages', () => {
    render(<StatusProgress status="Cancelled" steps={['Pending', 'Confirmed', 'Completed']} />);
    expect(screen.getByText('Cancelled')).toBeVisible();
    expect(screen.queryByText('Completed')).toBeNull();
  });
  it('announces confirmed saves and allows dismissal', () => {
    render(<SuccessNotice />);
    expect(screen.queryByRole('status')).toBeNull();
    act(announceSuccess);
    expect(screen.getByRole('status')).toHaveTextContent('Changes saved successfully.');
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss message' }));
    expect(screen.queryByRole('status')).toBeNull();
  });
});
