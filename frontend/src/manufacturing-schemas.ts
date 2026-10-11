import { z } from 'zod';
const quantity = z.number().int().min(1).max(2147483647);
export const bomSchema = z.object({ productId: z.string().uuid(), version: z.string().trim().min(1).max(40), name: z.string().trim().min(1).max(150), baseQuantity: quantity,
  items: z.array(z.object({ materialProductId: z.string().uuid(), quantityRequired: quantity })).min(1).max(100)
}).refine(v => new Set(v.items.map(i => i.materialProductId)).size === v.items.length && v.items.every(i => i.materialProductId !== v.productId), {path:['items'], message:'Select distinct raw materials.'});
export const productionSchema = z.object({productId:z.string().uuid(),billOfMaterialsId:z.string().uuid(),plannedQuantity:quantity,sourceWarehouseId:z.string().uuid(),destinationWarehouseId:z.string().uuid(),
  plannedStartDate:z.string(),plannedEndDate:z.string(),notes:z.string().trim().max(2000)
}).refine(v => !v.plannedStartDate || !v.plannedEndDate || v.plannedEndDate >= v.plannedStartDate, {path:['plannedEndDate'],message:'Planned end must not precede planned start.'});
