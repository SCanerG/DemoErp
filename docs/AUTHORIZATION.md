# Authorization and user management

[README](../README.md) · [Security](SECURITY.md) · [Audit logging](AUDIT_LOGGING.md)

The application has one predefined enum role per user: Viewer, Manager or Admin.
ASP.NET Core policies enforce access on controllers/actions; browser visibility is
only a usability feature. Business records remain shared, without tenant ownership.

## Implemented permission matrix

| Operation | Admin | Manager | Viewer | Policy |
| --- | --- | --- | --- | --- |
| Read products/categories/customers | Yes | Yes | Yes | BusinessRead |
| Create/edit products/categories/customers | Yes | Yes | No | BusinessWrite |
| Delete products/categories/customers | Yes | No | No | BusinessDelete |
| Read orders | Yes | Yes | Yes | BusinessRead |
| Create/confirm/complete/cancel orders | Yes | Yes | No | OrderManage |
| Read inventory and movements | Yes | Yes | Yes | BusinessRead |
| Read dashboard/reports and CSV exports | Yes | Yes | Yes | BusinessRead |
| AI status and allowlisted read-only tools | Yes | Yes | Yes | BusinessRead, rechecked per tool and final response |
| Stock in/out/adjust/minimum level | Yes | Yes | No | InventoryManage |
| Read/create/edit users | Yes | No | No | UserManage |
| Change roles or account active status | Yes | No | No | UserManage |
| Read audit list/detail | Yes | No | No | AuditRead |

Policy names and role membership are centralized in AccessPolicies. A class-level
BusinessRead policy and action-level write/delete policies are cumulative. The
frontend uses permissions.tsx, Can and RequirePermission. Viewer sees read-only
screens, Manager operational actions, and Admin user/audit navigation. Direct URLs
to forbidden screens render localized Access Denied; the API still returns 403.

## User API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | /api/users | Safe user DTO list |
| GET | /api/users/{id} | Safe user DTO detail |
| POST | /api/users | Explicit administrative account creation |
| PUT | /api/users/{id} | Name/email only |
| PUT | /api/users/{id}/role | Predefined role only |
| PUT | /api/users/{id}/status | IsActive only |

There is no physical delete or password-reset endpoint. DTOs return Id, Name, Email,
Role, IsActive, CreatedAt and UpdatedAt, never PasswordHash or SecurityVersion.
Create accepts name/email/password/role; the Admin-only action explicitly authorizes
creation of another Admin. The UI requires additional confirmation for creating an
Admin and for role/status changes. General editing cannot overpost role, active
status, password, bootstrap marker or security version.

Public registration accepts only name/email/password and always creates an active
Viewer. Unknown JSON fields are ignored rather than trusted. Existing accounts are
backfilled as Viewer; deployment does not promote any account automatically.

Emails are trimmed and normalized to lowercase. FluentValidation checks names,
email/password lengths and role enum values. A unique database email index remains
authoritative during public registration/admin-creation races. Unknown/numeric enum
values and missing required role/status fields return 400.

## Last active Admin and concurrency

Users cannot change their own role or deactivate themselves. Under one ReadCommitted
transaction, every user-management write acquires PostgreSQL transaction advisory
lock `7419202604`. Bootstrap uses the same lock. This serializes administrative
changes across API processes, rather than relying on an in-memory lock or an
unprotected count. After acquiring it, the service rechecks the actor's current
active Admin role and SecurityVersion. A request authenticated before another
request revoked that actor cannot continue with stale administrative authority.

The target is then loaded and the active-Admin count checked before an active Admin
can be demoted/deactivated. With self-protection and actor revalidation, the only
remaining Admin cannot remove itself; the count check is an additional safeguard.
Role/status/email changes increment SecurityVersion. Changes and their audit rows
commit atomically. Repeating a role/status value does not increment the version or
create a success audit event when no allowlisted business field changed.

Database-owner SQL can bypass application rules; this is not a claim that arbitrary
administrative SQL cannot remove the last Admin. No API provides that bypass.

## Frontend synchronization

User mutations refresh user list/detail and audit queries. Editing one's own name
refreshes the displayed session user; editing one's email signs out because that
security change invalidates the existing token. A protected-request 401 clears only
the matching current session and cache, while a 403 displays an access error without
logging out. Old stored sessions without the new role metadata require login again.

Real PostgreSQL integration tests exercise all roles, endpoint restrictions,
overposting, stale tokens and competing Admin changes. Playwright verifies actual
role sessions, hidden actions, denied routes, user forms/dialogs, audit details,
TR/EN mobile layouts and distinct 401/403 behavior. See [verification](../VERIFICATION.md).
