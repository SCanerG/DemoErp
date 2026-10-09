export type UserRole = 'Admin' | 'Manager' | 'Viewer';
export interface User { id: string; name: string; email: string; role: UserRole; isActive: boolean; createdAt: string; updatedAt: string | null }
export interface AuditLog { id: string; userId: string | null; userName: string; action: string; entityName: string; entityId: string; oldValues: Record<string, unknown> | null; newValues: Record<string, unknown> | null; description: string; createdAt: string; correlationId: string | null }
export interface AuditPage { items: AuditLog[]; page: number; pageSize: number; totalCount: number }
export interface Session { accessToken: string; expiresAt: string; user: User }
export interface Product {
  categoryId: string;
  categoryName: string;
  id: string;
  name: string;
  description: string;
  price: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}
export interface ProductInput { name: string; description: string; price: number; isActive: boolean; categoryId: string }
export interface Category { id: string; name: string; description: string; isActive: boolean; createdAt: string; updatedAt: string | null; productCount: number }
export interface Customer { id: string; name: string; email: string; phone: string; address: string; isActive: boolean; createdAt: string; updatedAt: string | null; orderCount: number }
export type OrderStatus = 'Pending' | 'Confirmed' | 'Completed' | 'Cancelled';
export interface OrderSummary { id: string; orderNumber: string; customerId: string; customerName: string; status: OrderStatus; orderDate: string; totalAmount: number; itemCount: number }
export interface OrderLine { id: string; productId: string; productName: string; quantity: number; unitPrice: number; lineTotal: number }
export interface Order extends Omit<OrderSummary, 'itemCount'> { createdAt: string; updatedAt: string | null; items: OrderLine[]; inventoryWasDeducted: boolean }
export interface Inventory { id: string; productId: string; productName: string; categoryName: string; isActive: boolean; quantityOnHand: number; minimumStockLevel: number; updatedAt: string }
export type MovementType = 'StockIn' | 'StockOut' | 'AdjustmentIncrease' | 'AdjustmentDecrease' | 'OrderDeduction' | 'OrderCancellationReturn';
export interface InventoryMovement { id: string; productId: string; movementType: MovementType; quantity: number; quantityBefore: number; quantityAfter: number; referenceType: 'Manual' | 'Order'; referenceId: string | null; orderNumber: string | null; reason: string; createdAt: string; createdByUserId: string; performedBy: string }
