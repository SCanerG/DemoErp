# UI design and accessibility

[README](../README.md) · [Testing](TESTING.md)

## Direction and tokens

The interface uses forest green for primary actions and navigation, white surfaces, pale neutral backgrounds, subtle borders and restrained shadows. Segoe UI/Helvetica/Arial avoids external font requests. `frontend/src/styles.css` defines semantic colors, surfaces, foreground/muted text, borders, focus, status colors, spacing, typography, radii and shadows centrally. Existing Tailwind utilities remain for local structure; this phase does not claim every legacy utility has been replaced.

## Application shell

`AppShell.tsx` groups implemented routes into overview, sales, catalog, inventory, procurement, manufacturing, analytics, assistance and administration. Native SVG icons share stroke styling. Active links retain text and an indicator. Desktop collapse keeps accessible link names and titles; mobile uses a native modal dialog with Escape, focus restoration and scrollable navigation. A skip link targets the main content. Navigation visibility uses the existing permission matrix; backend policies remain authoritative.

The header shows route context, language controls, user/role details and logout. The account disclosure uses native keyboard interaction. It is deliberately not a notification center. Sidebar/header components are shared across business pages; existing forms, routes, query parameters and transitions remain in place.

## Business screens

Dashboard data and metric definitions are unchanged. Eight KPI cards precede sales/status charts, side-by-side product/customer sections on wide screens, stock alerts and a separate operational overview. Stable skeletons describe loading without showing fabricated figures. Charts use semantic tokens and retain readable chart-data alternatives.

Tables share header density, borders, hover feedback and numeric typography. Wide line-item/movement/report tables scroll within their panel; mobile sales/material screens retain existing cards. Server pagination/filter/sort behavior is unchanged. Forms share input height, labels, focus, errors and disabled styling while retaining React Hook Form/Zod. Successful mutations announce a dismissible message only after an API success; failed operations and AI questions do not show a save success.

Sales, purchasing and production detail pages include compact workflow steps that mark the **actual current state**. They describe the lifecycle, not fabricated historical events. Existing metadata, receipt/movement links and immutable histories remain visible. Status text remains readable independently of color: neutral draft/inactive, amber pending/partial/shortage, blue confirmed/released/submitted, teal in progress, green completed/received/available and muted red cancelled/out of stock.

## Localization and accessibility

English/Turkish text is centralized in `translations.ts`. Language switching updates document language and visible copy. Number, currency and date formatting use the selected locale; report date boundaries continue their existing UTC definitions. No language-specific component copies were introduced.

Native buttons, labeled controls, semantic tables, dialog focus trapping, skip links and visible focus support keyboard use. Reduced-motion disables animation and transitions. Axe found two contrast issues during implementation (login caption and back links); both were corrected and the representative scan was rerun without violations. The automated scope includes login, dashboard, product list/form, order detail, delete dialog and mobile navigation. This is not a claim of comprehensive WCAG 2.2 AA conformance; screen-reader and broader manual audits remain useful.

## Rendered verification

The portfolio test renders eight representative workflows at 1440, 1024, 768 and 390 pixels and checks document overflow. Actual PNGs are under `docs/screenshots/`; the `qa-*` files record width checks and the named files provide README examples. Data is synthetic and is created through real APIs in a disposable stack. Screenshots are not generated mockups.

Visual inspection includes desktop dashboard, 1024 dashboard, 768 product form and mobile production detail. Wide tables intentionally scroll inside cards; they do not force document scrolling. A long dashboard remains a vertically scrollable operational page.
