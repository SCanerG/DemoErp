import { z } from 'zod';
export const purchaseStates = ['Draft', 'Submitted', 'PartiallyReceived', 'Received', 'Cancelled'] as const;
export type PurchaseState = typeof purchaseStates[number];
export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }
export interface Supplier { id: string; supplierCode: string; name: string; contactPerson: string; email: string; phone: string; address: string; taxNumber: string; isActive: boolean; createdAt: string; updatedAt: string | null }
export interface PurchaseSummary { id: string; purchaseOrderNumber: string; supplierId: string; supplierName: string; status: PurchaseState; orderDate: string; expectedDeliveryDate: string | null; totalAmount: number; orderedQuantity: number; receivedQuantity: number; remainingQuantity: number }
export interface PurchaseItem { id: string; productId: string; productName: string; quantityOrdered: number; quantityReceived: number; quantityRemaining: number; unitCost: number; lineTotal: number }
export interface Purchase extends PurchaseSummary { notes: string; createdBy: string; items: PurchaseItem[] }
export interface ReceiptSummary { id: string; receiptNumber: string; receivedAt: string; createdBy: string; totalReceivedQuantity: number; itemCount: number }
export interface Receipt extends ReceiptSummary { warehouseId: string; warehouseName: string; purchaseOrderId: string; purchaseOrderNumber: string; supplierId: string; supplierName: string; notes: string; items: { id: string; purchaseOrderItemId: string; productId: string; productName: string; quantityReceived: number }[] }
export interface ProcurementSummary { openPurchaseOrders: number; partiallyReceivedOrders: number; receivedOrders: number; activeSuppliers: number; outstandingQuantity: number }
export const supplierSchema = z.object({ supplierCode: z.string().trim().min(1, 'Required').max(40).regex(/^[a-zA-Z0-9][a-zA-Z0-9_-]*$/, 'Use letters, numbers, hyphens or underscores.'), name: z.string().trim().min(1, 'Required').max(150), contactPerson: z.string().max(150), email: z.union([z.literal(''), z.email('Enter a valid email address.')]).refine(s => s.length <= 254), phone: z.string().max(40), address: z.string().max(1000), taxNumber: z.string().max(40) });
export const purchaseSchema = z.object({ supplierId: z.string().min(1, 'Select a supplier.'), expectedDeliveryDate: z.string(), notes: z.string().max(2000), items: z.array(z.object({ productId: z.string().min(1, 'Select a product.'), quantityOrdered: z.number().int().min(1).max(100000), unitCost: z.number().min(0).max(9999999999.99).refine(n => Math.abs(n * 100 - Math.round(n * 100)) < 0.0001, 'Use at most two decimal places.') })).min(1, 'Add at least one item.').max(100) }).refine(v => new Set(v.items.map(i => i.productId)).size === v.items.length, { path: ['items'], message: 'Choose each product only once.' });
export type PurchaseInput = z.infer<typeof purchaseSchema>;
export const receiptSchema = (items: PurchaseItem[]) => z.object({ notes: z.string().max(1000), quantities: z.array(z.number().int().min(0).max(100000)) }).superRefine((v, ctx) => {
  if (!v.quantities.some(q => q > 0)) ctx.addIssue({ code: 'custom', path: ['quantities'], message: 'Enter at least one positive receipt quantity.' });
  if (v.quantities.length !== items.length) ctx.addIssue({ code: 'custom', path: ['quantities'], message: 'Check the submitted fields.' });
  v.quantities.forEach((q, i) => { if (q > (items[i]?.quantityRemaining ?? 0)) ctx.addIssue({ code: 'custom', path: ['quantities', i], message: 'Quantity exceeds remaining amount.' }); });
});
export const progress = (ordered: number, received: number) => ordered > 0 ? Math.min(100, Math.round(received / ordered * 100)) : 0;
export const canReceive = (status: PurchaseState) => status === 'Submitted' || status === 'PartiallyReceived';
export const canCancel = (status: PurchaseState) => status === 'Draft' || status === 'Submitted';
export const previewTotal = (items: PurchaseInput['items']) => items.reduce((s, i) => s + (Number.isFinite(i.unitCost) && Number.isFinite(i.quantityOrdered) ? Math.round(Math.max(0, i.unitCost) * Math.max(0, i.quantityOrdered) * 100) : 0), 0) / 100;
// A ref-held operation survives failed sends; changing the normalized payload issues a new key.
export function receiptOperation(previous: { payload: string; key: string } | null, payload: string) { return previous?.payload === payload ? previous : { payload, key: crypto.randomUUID() }; }
