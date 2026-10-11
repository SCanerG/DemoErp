import { Suspense, useEffect, useRef, useState } from 'react';
import { Link, NavLink, Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './Auth';
import { SuccessNotice } from './SuccessNotice';
import { Loading } from './components';
import { LanguageSelector, t } from './i18n';
import { allowed, type Permission } from './permissions';

const groups: { label: string; links: [string, string, Permission?][] }[] = [
  { label: 'Overview', links: [['dashboard', 'Dashboard']] },
  { label: 'Sales', links: [['customers', 'Customers'], ['orders', 'Orders']] },
  { label: 'Catalog management', links: [['products', 'Products'], ['categories', 'Categories']] },
  { label: 'Inventory', links: [['inventory', 'Inventory'], ['warehouses', 'Warehouses'], ['stock-transfers', 'Stock transfers']] },
  { label: 'Procurement', links: [['suppliers', 'Suppliers'], ['purchase-orders', 'Purchase orders']] },
  { label: 'Manufacturing', links: [['boms', 'Bill of Materials'], ['production-orders', 'Production Orders']] },
  { label: 'Analytics', links: [['reports', 'Reports']] },
  { label: 'Assistance', links: [['ai-assistant', 'AI Assistant']] },
  { label: 'Administration', links: [['users', 'Users', 'users'], ['audit-logs', 'Audit Logs', 'audit']] },
];
const icons: Record<string, string> = {
  dashboard: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  products: 'M12 3 3 8l9 5 9-5-9-5z M3 8v9l9 5 9-5V8 M12 13v9',
  categories: 'M3 3h6v6H3z M15 3h6v6h-6z M3 15h6v6H3z M15 15h6v6h-6z',
  customers: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M17 4a4 4 0 0 1 0 7 M22 21v-2a4 4 0 0 0-3-4',
  inventory: 'M4 4h16v16H4z M4 9h16 M9 9v11 M14 13h3 M14 16h3',
  warehouses: 'M3 21V9l9-6 9 6v12 M7 21V11h10v10 M7 15h10 M7 18h10',
  'stock-transfers': 'M3 7h17l-4-4 M21 17H4l4 4 M20 7l-4 4 M4 17l4-4',
  reports: 'M4 3v18h17 M8 16v-5 M13 16V7 M18 16V4',
  boms: 'M12 3v6 M4 21v-6h16v6 M12 9v12 M8 3h8v6H8z',
  'production-orders': 'M3 21V10l6 4V10l6 4V3h5v18H3z M7 17v1 M12 17v1 M17 17v1',
  'ai-assistant': 'M12 3v3 M8 3h8 M4 8h16v12H4z M8 12h1 M15 12h1 M8 16h8',
};
function Icon({ name }: { name: string }) {
  return <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={icons[name] ?? 'M5 3h14v18H5z M8 8h8 M8 12h8 M8 16h5'} /></svg>;
}

export function AppShell() {
  const { session, logout } = useAuth();
  const location = useLocation();
  const [collapsed, setCollapsed] = useState(false);
  const [mobile, setMobile] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (mobile) dialog.current?.showModal();
    else if (dialog.current?.open) { dialog.current.close(); trigger.current?.focus(); }
  }, [mobile]);
  useEffect(() => { setMobile(false); }, [location.pathname]);
  if (!session) return <Navigate to="/auth/login" replace />;
  const title = groups.flatMap(g => g.links).find(([path]) => location.pathname.split('/')[1] === path)?.[1] ?? 'Business workspace';
  const navigation = <nav aria-label={t('Main')} className="shell-nav">{groups.map(group => {
    const links = group.links.filter(([, , permission]) => allowed(session.user, permission ?? 'read'));
    return links.length ? <div className="nav-group" key={group.label}><p className="nav-group-title">{t(group.label)}</p>{links.map(([path, label]) => <NavLink key={path} to={`/${path}`} title={t(label)} className={({ isActive }) => `shell-link ${isActive ? 'selected' : ''}`}><Icon name={path} /><span>{t(label)}</span></NavLink>)}</div> : null;
  })}</nav>;
  const brand = <Link to="/dashboard" className="brand shell-brand"><span className="brand-icon">D</span><span>DemoErp<small>{t('Business workspace')}</small></span></Link>;
  return <div className={`app-shell ${collapsed ? 'is-collapsed' : ''}`}>
    <a href="#main-content" className="skip-link">{t('Skip to content')}</a>
    <aside className="desktop-sidebar">{brand}{navigation}<button className="collapse-control" aria-label={t(collapsed ? 'Expand navigation' : 'Collapse navigation')} aria-expanded={!collapsed} onClick={() => setCollapsed(v => !v)}><span aria-hidden="true">{collapsed ? '»' : '«'}</span><span>{t('Collapse navigation')}</span></button></aside>
    <div className="shell-body"><header className="shell-header"><button ref={trigger} className="button secondary mobile-menu" aria-label={t('Open navigation')} aria-expanded={mobile} onClick={() => setMobile(true)}>☰</button><div className="header-page"><span>{t('Business workspace')}</span><p>{t(title)}</p></div><div className="header-actions"><LanguageSelector /><details className="account-menu"><summary aria-label={t('Account')}><span className="avatar">{session.user.name.slice(0, 1).toUpperCase()}</span><span className="account-name">{session.user.name}<small>{t(session.user.role)}</small></span></summary><div className="account-popover"><strong>{session.user.name}</strong><p>{t(session.user.role)}</p><p className="break-all">{session.user.email}</p></div></details><button className="logout-control" onClick={logout}>{t('Sign out')}</button></div></header>
      <SuccessNotice /><main className="workspace" id="main-content" tabIndex={-1}><Suspense fallback={<Loading />}><Outlet /></Suspense></main><footer className="shell-footer">DemoErp <span>· {t('Business workspace')}</span></footer>
    </div>
    <dialog ref={dialog} className="mobile-drawer" aria-label={t('Main')} onCancel={e => { e.preventDefault(); setMobile(false); }}><div className="drawer-heading">{brand}<button className="button secondary" aria-label={t('Close navigation')} onClick={() => setMobile(false)}>×</button></div>{mobile && navigation}</dialog>
  </div>;
}
