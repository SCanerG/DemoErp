namespace Demo.Api.AI.Agents;
public static class AgentFallback
{
    public static string Summary(InventoryAnalysis? inventory,SalesAnalysis? sales,ResolvedOrderDraft? draft,string language)
    {
        var tr=language=="tr";var sentences=new List<string>{tr?AiAnswerComposer.TurkishFallback:AiAnswerComposer.EnglishFallback};
        if(sales is not null)sentences.Add(tr?$"Seçilen dönemde {sales.Summary.Current.CompletedOrders} sipariş tamamlandı; kayıtlı satış tutarı {sales.Summary.Current.Sales} USD. Ayrıntıları satış tablosunda inceleyebilirsiniz.":$"The selected period contains {sales.Summary.Current.CompletedOrders} completed orders and recorded sales of {sales.Summary.Current.Sales} USD. Review the sales table for details.");
        if(inventory is not null) {
            foreach(var row in inventory.Products.Take(3))sentences.Add(tr?$"{row.Product} ürününün mevcut stoğu {row.CurrentStock} adet. Minimum stok seviyesi {row.MinimumStock} adet; geçmiş tamamlanmış satış miktarı {row.CompletedQuantity} adet. İkmal önerisini aşağıdaki doğrulanmış tabloda inceleyin.":$"{row.Product} currently has {row.CurrentStock} units. Its minimum stock is {row.MinimumStock}, and historical completed sales total {row.CompletedQuantity} units. Review the verified replenishment table below.");
            if(inventory.Products.Count==0)sentences.Add(tr?"Seçili politikaya göre ikmal gerektiren ürün bulunamadı.":"No products require replenishment under the selected policy.");
        }
        if(draft is not null)sentences.Add(tr?"Sipariş taslağı ERP referanslarıyla hazırlandı. Müşteriyi, ürünleri, fiyatları ve stokları inceleyin. Sipariş gönderilmedi; hiçbir iş kaydı değiştirilmedi.":"The order preview was prepared from ERP references. Review customer, products, prices and availability. No order was submitted and no business record was changed.");
        return string.Join("\n\n",sentences);
    }
}
