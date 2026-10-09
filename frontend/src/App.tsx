import { Can, RequirePermission } from './permissions';
import { UsersPage, UserEditor, AuditPageView, AuditDetail } from './pages/Administration';
import { t, useLanguage, LanguageSelector } from './i18n';
import { lazy, Suspense, useEffect } from 'react';
import { Loading } from './components';
const DashboardPage = lazy(() => import('./pages/Reporting').then(m => ({ default: m.DashboardPage })));
const ReportsPage = lazy(() => import('./pages/Reporting').then(m => ({ default: m.ReportsPage })));
const AiAssistant = lazy(() => import('./pages/AiAssistant'));
import { Link, NavLink, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { RecordList, RecordEditor, RecordDetail } from './pages/Records';
import { OrderList, OrderCreate, OrderDetail } from './pages/Orders';
import { InventoryList, InventoryDetail } from './pages/Inventory';
import { useAuth } from './Auth';
import { AuthPage } from './pages/AuthPage';
import { ProductList } from './pages/ProductList';
import { ProductDetail } from './pages/ProductDetail';
import { ProductEditor } from './pages/ProductEditor';

function ProtectedLayout() {
  const { session, logout } = useAuth();
  if (!session) return <Navigate to="/auth/login" replace />;
  return <div className="min-h-screen"><header className="app-header"><div className="header-inner">
    <Link className="brand" to="/dashboard"><span className="brand-icon">{t("C")}</span>{t("Catalog")}<span className="brand-dot">.</span></Link>
    <div className="ml-auto flex items-center gap-3"><span className="hidden max-w-40 truncate text-sm text-slate-500 md:block">{session.user.name}</span><span className="avatar" aria-label={session.user.name}>{session.user.name.slice(0, 1).toUpperCase()}</span><LanguageSelector /><button className="text-sm text-slate-500 hover:text-emerald-800" onClick={logout}>{t("Sign out")}</button></div>
    <nav className="business-nav" aria-label={t("Main")}>{[['dashboard', 'Dashboard'], ['products', 'Products'], ['categories', 'Categories'], ['customers', 'Customers'], ['orders', 'Orders'], ['inventory', 'Inventory'], ['reports', 'Reports']].map(([path, label]) => <NavLink key={path} className={({ isActive }) => `nav-link ${isActive ? 'selected' : ''}`} to={`/${path}`}>{t(label)}</NavLink>)}<Can permission="read"><NavLink className={({ isActive }) => `nav-link ${isActive ? 'selected' : ''}`} to="/ai-assistant">{t("AI Assistant")}</NavLink></Can><Can permission="users"><NavLink className={({ isActive }) => `nav-link ${isActive ? 'selected' : ''}`} to="/users">{t("Users")}</NavLink></Can><Can permission="audit"><NavLink className={({ isActive }) => `nav-link ${isActive ? 'selected' : ''}`} to="/audit-logs">{t("Audit Logs")}</NavLink></Can></nav>
  </div></header><main className="workspace"><Suspense fallback={<Loading />}><Outlet /></Suspense></main><footer className="mx-auto max-w-6xl px-6 pb-8 text-xs text-slate-400">{t("Catalog · A simple product workspace")}</footer></div>;
}

export function App() {
  const language = useLanguage();
  useEffect(() => { document.title = t('Catalog · A simple product workspace'); }, [language]);
  return <Routes>
    <Route path="/" element={<Navigate to="/dashboard" replace />} />
    <Route path="/auth/login" element={<AuthPage />} />
    <Route path="/auth/register" element={<AuthPage registerMode />} />
    <Route element={<ProtectedLayout />}>
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="/ai-assistant" element={<RequirePermission permission="read"><AiAssistant /></RequirePermission>} />
      <Route path="/reports" element={<Navigate to="/reports/sales" replace />} />
      <Route path="/reports/:kind" element={<ReportsPage />} />
      <Route path="/products" element={<ProductList />} />
      <Route path="/products/new" element={<RequirePermission permission="write"><ProductEditor /></RequirePermission>} />
      <Route path="/products/:id" element={<ProductDetail />} />
      <Route path="/products/:id/edit" element={<RequirePermission permission="write"><ProductEditor /></RequirePermission>} />
      {(['categories', 'customers'] as const).map(kind => <Route key={kind}>
        <Route path={`/${kind}`} element={<RecordList kind={kind} />} />
        <Route path={`/${kind}/new`} element={<RequirePermission permission="write"><RecordEditor kind={kind} /></RequirePermission>} />
        <Route path={`/${kind}/:id`} element={<RecordDetail kind={kind} />} />
        <Route path={`/${kind}/:id/edit`} element={<RequirePermission permission="write"><RecordEditor kind={kind} /></RequirePermission>} />
      </Route>)}
      <Route path="/orders" element={<OrderList />} /><Route path="/orders/new" element={<RequirePermission permission="write"><OrderCreate /></RequirePermission>} /><Route path="/orders/:id" element={<OrderDetail />} />
      <Route path="/inventory" element={<InventoryList />} /><Route path="/inventory/:productId" element={<InventoryDetail />} />
      <Route path="/users" element={<RequirePermission permission="users"><UsersPage /></RequirePermission>} />
      <Route path="/users/new" element={<RequirePermission permission="users"><UserEditor /></RequirePermission>} />
      <Route path="/users/:id/edit" element={<RequirePermission permission="users"><UserEditor /></RequirePermission>} />
      <Route path="/audit-logs" element={<RequirePermission permission="audit"><AuditPageView /></RequirePermission>} />
      <Route path="/audit-logs/:id" element={<RequirePermission permission="audit"><AuditDetail /></RequirePermission>} />
    </Route>
    <Route path="*" element={<main className="workspace"><h1 className="text-3xl font-semibold">{t("Page not found")}</h1><p className="my-4 text-slate-500">{t("This page does not exist.")}</p><Link className="button" to="/products">{t("Go to products")}</Link></main>} />
  </Routes>;
}
