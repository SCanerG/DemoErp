import { RequirePermission } from './permissions';
import { t, useLanguage } from './i18n';
import { lazy, Suspense, useEffect } from 'react';
import { Loading } from './components';
import { Link, Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from './AppShell';

const BomsPage = lazy(() => import('./pages/Boms').then(m => ({ default: m.BomsPage })));
const BomEditor = lazy(() => import('./pages/Boms').then(m => ({ default: m.BomEditor })));
const BomDetail = lazy(() => import('./pages/Boms').then(m => ({ default: m.BomDetail })));
const ProductionPage = lazy(() => import('./pages/Production').then(m => ({ default: m.ProductionPage })));
const ProductionEditor = lazy(() => import('./pages/Production').then(m => ({ default: m.ProductionEditor })));
const ProductionDetail = lazy(() => import('./pages/Production').then(m => ({ default: m.ProductionDetail })));
const ManufacturingReport = lazy(() => import('./pages/ManufacturingReports').then(m => ({ default: m.ManufacturingReport })));
const WarehousesPage = lazy(() => import('./pages/Warehouses').then(m => ({ default: m.WarehousesPage })));
const WarehouseEditor = lazy(() => import('./pages/Warehouses').then(m => ({ default: m.WarehouseEditor })));
const WarehouseDetail = lazy(() => import('./pages/Warehouses').then(m => ({ default: m.WarehouseDetail })));
const TransfersPage = lazy(() => import('./pages/Transfers').then(m => ({ default: m.TransfersPage })));
const TransferEditor = lazy(() => import('./pages/Transfers').then(m => ({ default: m.TransferEditor })));
const TransferDetail = lazy(() => import('./pages/Transfers').then(m => ({ default: m.TransferDetail })));
const UsersPage = lazy(() => import('./pages/Administration').then(m => ({ default: m.UsersPage })));
const UserEditor = lazy(() => import('./pages/Administration').then(m => ({ default: m.UserEditor })));
const AuditPageView = lazy(() => import('./pages/Administration').then(m => ({ default: m.AuditPageView })));
const AuditDetail = lazy(() => import('./pages/Administration').then(m => ({ default: m.AuditDetail })));
const DashboardPage = lazy(() => import('./pages/Reporting').then(m => ({ default: m.DashboardPage })));
const ReportsPage = lazy(() => import('./pages/Reporting').then(m => ({ default: m.ReportsPage })));
const SuppliersPage = lazy(() => import('./pages/Procurement').then(m => ({ default: m.SuppliersPage })));
const SupplierEditor = lazy(() => import('./pages/Procurement').then(m => ({ default: m.SupplierEditor })));
const SupplierDetail = lazy(() => import('./pages/Procurement').then(m => ({ default: m.SupplierDetail })));
const PurchaseList = lazy(() => import('./pages/Procurement').then(m => ({ default: m.PurchaseList })));
const PurchaseEditor = lazy(() => import('./pages/Procurement').then(m => ({ default: m.PurchaseEditor })));
const PurchaseDetail = lazy(() => import('./pages/Procurement').then(m => ({ default: m.PurchaseDetail })));
const ReceiveGoods = lazy(() => import('./pages/Procurement').then(m => ({ default: m.ReceiveGoods })));
const ReceiptDetail = lazy(() => import('./pages/Procurement').then(m => ({ default: m.ReceiptDetail })));
const KnowledgeCenter = lazy(() => import('./pages/KnowledgeCenter'));
const OperationsCenter = lazy(() => import('./pages/OperationsCenter'));
const QualityDashboard = lazy(() => import('./pages/QualityDashboard'));
const WorkflowDesigner = lazy(() => import('./pages/WorkflowDesigner'));
const AgentCenter = lazy(() => import('./pages/AgentCenter'));
const AiAssistant = lazy(() => import('./pages/AiAssistant'));
const RecordList = lazy(() => import('./pages/Records').then(m => ({ default: m.RecordList })));
const RecordEditor = lazy(() => import('./pages/Records').then(m => ({ default: m.RecordEditor })));
const RecordDetail = lazy(() => import('./pages/Records').then(m => ({ default: m.RecordDetail })));
const OrderList = lazy(() => import('./pages/Orders').then(m => ({ default: m.OrderList })));
const OrderCreate = lazy(() => import('./pages/Orders').then(m => ({ default: m.OrderCreate })));
const OrderDetail = lazy(() => import('./pages/Orders').then(m => ({ default: m.OrderDetail })));
const InventoryList = lazy(() => import('./pages/Inventory').then(m => ({ default: m.InventoryList })));
const InventoryDetail = lazy(() => import('./pages/Inventory').then(m => ({ default: m.InventoryDetail })));
const AuthPage = lazy(() => import('./pages/AuthPage').then(m => ({ default: m.AuthPage })));
const ProductList = lazy(() => import('./pages/ProductList').then(m => ({ default: m.ProductList })));
const ProductDetail = lazy(() => import('./pages/ProductDetail').then(m => ({ default: m.ProductDetail })));
const ProductEditor = lazy(() => import('./pages/ProductEditor').then(m => ({ default: m.ProductEditor })));


export function App() {
  const language = useLanguage();
  useEffect(() => { document.title = 'DemoErp · ' + t('Business workspace'); }, [language]);
  return <Suspense fallback={<Loading />}><Routes>
    <Route path="/" element={<Navigate to="/dashboard" replace />} />
    <Route path="/auth/login" element={<AuthPage />} />
    <Route path="/auth/register" element={<AuthPage registerMode />} />
    <Route element={<AppShell />}>
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="/knowledge" element={<KnowledgeCenter />} />
      <Route path="/operations" element={<OperationsCenter />} />
      <Route path="/ai-quality" element={<QualityDashboard />} />
      <Route path="/workflows" element={<WorkflowDesigner />} />
      <Route path="/agent-center" element={<AgentCenter />} />
      <Route path="/ai-assistant" element={<RequirePermission permission="read"><AiAssistant /></RequirePermission>} />
      <Route path="/reports" element={<Navigate to="/reports/sales" replace />} />
      <Route path="/reports/procurement" element={<PurchaseList report />} />
      <Route path="/suppliers" element={<SuppliersPage />} />
      <Route path="/suppliers/new" element={<RequirePermission permission="write"><SupplierEditor /></RequirePermission>} />
      <Route path="/suppliers/:id" element={<SupplierDetail />} />
      <Route path="/suppliers/:id/edit" element={<RequirePermission permission="write"><SupplierEditor /></RequirePermission>} />
      <Route path="/purchase-orders" element={<PurchaseList />} />
      <Route path="/purchase-orders/new" element={<RequirePermission permission="write"><PurchaseEditor /></RequirePermission>} />
      <Route path="/purchase-orders/:id" element={<PurchaseDetail />} />
      <Route path="/purchase-orders/:id/edit" element={<RequirePermission permission="write"><PurchaseEditor /></RequirePermission>} />
      <Route path="/purchase-orders/:id/receive" element={<RequirePermission permission="write"><ReceiveGoods /></RequirePermission>} />
      <Route path="/goods-receipts/:id" element={<ReceiptDetail />} />
      <Route path="/reports/warehouses" element={<WarehousesPage report />} />
      <Route path="/warehouses" element={<WarehousesPage />} />
      <Route path="/warehouses/new" element={<RequirePermission permission="users"><WarehouseEditor /></RequirePermission>} />
      <Route path="/warehouses/:id" element={<WarehouseDetail />} />
      <Route path="/warehouses/:id/edit" element={<RequirePermission permission="users"><WarehouseEditor /></RequirePermission>} />
      <Route path="/stock-transfers" element={<TransfersPage />} />
      <Route path="/stock-transfers/new" element={<RequirePermission permission="write"><TransferEditor /></RequirePermission>} />
      <Route path="/stock-transfers/:id" element={<TransferDetail />} />
      <Route path="/stock-transfers/:id/edit" element={<RequirePermission permission="write"><TransferEditor /></RequirePermission>} />
      <Route path="/boms" element={<BomsPage />} />
      <Route path="/boms/new" element={<RequirePermission permission="write"><BomEditor /></RequirePermission>} />
      <Route path="/boms/:id" element={<BomDetail />} />
      <Route path="/boms/:id/edit" element={<RequirePermission permission="write"><BomEditor /></RequirePermission>} />
      <Route path="/production-orders" element={<ProductionPage />} />
      <Route path="/production-orders/new" element={<RequirePermission permission="write"><ProductionEditor /></RequirePermission>} />
      <Route path="/production-orders/:id" element={<ProductionDetail />} />
      <Route path="/production-orders/:id/edit" element={<RequirePermission permission="write"><ProductionEditor /></RequirePermission>} />
      <Route path="/reports/material-requirements" element={<ManufacturingReport />} />
      <Route path="/reports/material-shortages" element={<ManufacturingReport shortages />} />
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
  </Routes></Suspense>;
}
