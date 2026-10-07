import { t, useLanguage, LanguageSelector } from './i18n';
import { useEffect } from 'react';
import { Link, NavLink, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { RecordList, RecordEditor, RecordDetail } from './pages/Records';
import { OrderList, OrderCreate, OrderDetail } from './pages/Orders';
import { useAuth } from './Auth';
import { AuthPage } from './pages/AuthPage';
import { ProductList } from './pages/ProductList';
import { ProductDetail } from './pages/ProductDetail';
import { ProductEditor } from './pages/ProductEditor';

function ProtectedLayout() {
  const { session, logout } = useAuth();
  if (!session) return <Navigate to="/auth/login" replace />;
  return <div className="min-h-screen"><header className="app-header"><div className="header-inner">
    <Link className="brand" to="/products"><span className="brand-icon">{t("C")}</span>{t("Catalog")}<span className="brand-dot">.</span></Link>
    <div className="ml-auto flex items-center gap-3"><span className="hidden max-w-40 truncate text-sm text-slate-500 md:block">{session.user.name}</span><span className="avatar" aria-label={session.user.name}>{session.user.name.slice(0, 1).toUpperCase()}</span><LanguageSelector /><button className="text-sm text-slate-500 hover:text-emerald-800" onClick={logout}>{t("Sign out")}</button></div>
    <nav className="business-nav" aria-label={t("Main")}>{[['products', 'Products'], ['categories', 'Categories'], ['customers', 'Customers'], ['orders', 'Orders']].map(([path, label]) => <NavLink key={path} className={({ isActive }) => `nav-link ${isActive ? 'selected' : ''}`} to={`/${path}`}>{t(label)}</NavLink>)}</nav>
  </div></header><main className="workspace"><Outlet /></main><footer className="mx-auto max-w-6xl px-6 pb-8 text-xs text-slate-400">{t("Catalog · A simple product workspace")}</footer></div>;
}

export function App() {
  const language = useLanguage();
  useEffect(() => { document.title = t('Catalog · A simple product workspace'); }, [language]);
  return <Routes>
    <Route path="/" element={<Navigate to="/products" replace />} />
    <Route path="/auth/login" element={<AuthPage />} />
    <Route path="/auth/register" element={<AuthPage registerMode />} />
    <Route element={<ProtectedLayout />}>
      <Route path="/products" element={<ProductList />} />
      <Route path="/products/new" element={<ProductEditor />} />
      <Route path="/products/:id" element={<ProductDetail />} />
      <Route path="/products/:id/edit" element={<ProductEditor />} />
      {(['categories', 'customers'] as const).map(kind => <Route key={kind}>
        <Route path={`/${kind}`} element={<RecordList kind={kind} />} />
        <Route path={`/${kind}/new`} element={<RecordEditor kind={kind} />} />
        <Route path={`/${kind}/:id`} element={<RecordDetail kind={kind} />} />
        <Route path={`/${kind}/:id/edit`} element={<RecordEditor kind={kind} />} />
      </Route>)}
      <Route path="/orders" element={<OrderList />} /><Route path="/orders/new" element={<OrderCreate />} /><Route path="/orders/:id" element={<OrderDetail />} />
    </Route>
    <Route path="*" element={<main className="workspace"><h1 className="text-3xl font-semibold">{t("Page not found")}</h1><p className="my-4 text-slate-500">{t("This page does not exist.")}</p><Link className="button" to="/products">{t("Go to products")}</Link></main>} />
  </Routes>;
}
