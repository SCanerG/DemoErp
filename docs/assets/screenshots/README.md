# Authentic application gallery

[Project overview](../../../README.md) · [Documentation](../../README.md)

Every image below originated in a real browser capture of DemoERP using synthetic records. No image-generation tool, fake UI, edited numerical value or fabricated result was used. Crops preserve original pixels; PNG files were compressed losslessly. Widths and height differ by captured content; all assets use ordinary GitHub Markdown image links, with full-width rendering and readable alt text.

## Featured views

![Executive dashboard showing actual synthetic KPI and sales-chart responses](dashboard-overview-en.png)

**Dashboard, English.** Cropped upper section of the existing 1440-pixel-wide synthetic browser capture. [Full page](dashboard-full-en.png).

![Agent Control Center with a completed local Sales and Inventory collaboration](agent-control-center-en.png)

**Agent Control Center, English, October 20, 2026.** Actual local `qwen3:8b` run with Sales, Inventory and a consolidated explanation. This historical capture represents that observed run, not a new performance measurement or a promise that every model interpretation is correct. It precedes later Knowledge/Quality navigation additions. Current behavior and later live checks are recorded in [verification](../../../VERIFICATION.md).

![Current Turkish visual workflow editor, registered node palette, connected canvas and review controls](workflow-designer-tr.png)

**Workflow Designer, Turkish, October 22, 2026.** Cropped detail from the actual Phase 11.8 browser acceptance. [Full page](workflow-designer-full-tr.png). The example's test name and notifications are synthetic.

## ERP workspaces

![Actual Inventory Agent stock and demand results with backend-calculated replenishment suggestions](agent-inventory-results-en.png)

**Inventory Agent detail.** A pixel-preserving 655 × 1060 crop from the same actual October 20 Sales + Inventory browser capture, focusing on current stock, minimum levels and demand-based recommendations. The original panel has horizontally overflowing columns; the crop does not invent or reflow values. These are historical synthetic facts, not a live data feed.

![Actual English warehouse inventory list and stock indicators](inventory-en.png)

![Actual English sales order workspace with synthetic records](orders-en.png)

![Actual production order page showing material consumption and finished-goods output](production-order-en.png)

## Provenance and publication boundaries

| Asset | Original source | Treatment |
| --- | --- | --- |
| `dashboard-overview-en.png` | Existing `docs/screenshots/dashboard-en.png` | Upper 1440 × 1200 crop |
| `dashboard-full-en.png` | Same existing dashboard capture | Full original content, lossless compression |
| `agent-control-center-en.png` | Preserved Phase 11.3 actual-model browser capture | Upper 1280 × 1260 crop; no text/result changes |
| `agent-inventory-results-en.png` | Same actual-model browser capture | Stock/demand/replenishment result panel, 655 × 1060 crop |
| `workflow-designer-tr.png` | Preserved Phase 11.8 actual-browser capture | Canvas/review detail, 1135 × 1210 |
| `workflow-designer-full-tr.png` | Same Phase 11.8 capture | Full 1440 × 2738 page |
| `inventory-en.png`, `orders-en.png`, `production-order-en.png` | Corresponding original `docs/screenshots/` assets | Full content, lossless compression |

Nine optimized PNG assets retain authentic application content. Original ERP screenshots remain in `docs/screenshots/`; original local execution evidence stays ignored. No secrets, personal customer records, browser error overlays or private server addresses are shown in the selected captures. The screenshot set is a product demonstration, not a real customer's business history. The ordinary order workspace illustrates where approved orders appear; it is not labelled as a capture of an agent approval or proof that its pictured sample rows were agent-created.

Fresh AI Assistant, Knowledge Center, Operations Center and Quality Dashboard screenshots are **not included**: no new database records or live queries were submitted during this documentation change solely to fill a gallery. Their implemented status is documented separately, and missing visual evidence is not replaced by mockups. A future refresh should use an explicitly isolated synthetic environment and consistent viewport sizes. [Capture/test procedure](../../DEVELOPMENT.md#screenshots).
