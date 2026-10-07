# API reference

[README](../README.md) · [Architecture](ARCHITECTURE.md) · [Database](DATABASE.md)

Default base URL: `http://localhost:5080/api`.
[Swagger UI](http://localhost:5080/swagger) and
[OpenAPI JSON](http://localhost:5080/swagger/v1/swagger.json) are enabled in Development.
Use Swagger's **Authorize** control with the login token. All business endpoints
require `Authorization: Bearer <token>`; registration/login and `/health` are public.
UUID route parameters are named `{id}` below.

## Authentication

| Method | Path | Request | Success |
| --- | --- | --- | --- |
| POST | `/auth/register` | name, email, password | 201 safe user DTO |
| POST | `/auth/login` | email, password | 200 token, expiry, safe user DTO |

Registration does not log the user in. Invalid login returns 401, duplicate email
409 and validation 400. Both endpoints share a 20 requests/IP/minute limiter (429).
There are no refresh, logout or password-reset API endpoints; UI logout is local.

## Products — Bearer required

| Method | Path | Result |
| --- | --- | --- |
| GET | `/products` | 200 list with category ID/name |
| GET | `/products/{id}` | 200 detail / 404 |
| POST | `/products` | 201 created detail |
| PUT | `/products/{id}` | 200 updated detail / 404 |
| DELETE | `/products/{id}` | 204 / 404 / 409 when referenced |

Create/update: `name`, `description`, `price`, `isActive`, `categoryId`. Price must
be nonnegative with at most two decimals. Category is required and active for new
assignments; an unchanged inactive category on an existing product is permitted.

## Categories — Bearer required

| Method | Path | Result |
| --- | --- | --- |
| GET | `/categories` | 200 list with product counts |
| GET | `/categories/{id}` | 200 detail / 404 |
| POST | `/categories` | 201 created detail |
| PUT | `/categories/{id}` | 200 updated detail / 404 |
| DELETE | `/categories/{id}` | 204 / 404 / 409 when referenced |

Create/update: `name`, `description`, `isActive`.

## Customers — Bearer required

| Method | Path | Result |
| --- | --- | --- |
| GET | `/customers` | 200 list with order counts |
| GET | `/customers/{id}` | 200 detail / 404 |
| POST | `/customers` | 201 created detail |
| PUT | `/customers/{id}` | 200 updated detail / 404 |
| DELETE | `/customers/{id}` | 204 / 404 / 409 when referenced |

Create/update: `name`, `email`, `phone`, `address`, `isActive`.
Email may be empty; a supplied value must be a valid address.

## Orders — Bearer required

| Method | Path | Result |
| --- | --- | --- |
| GET | `/orders` | 200 summaries with customer and item count |
| GET | `/orders/{id}` | 200 detail with lines / 404 |
| POST | `/orders` | 201 detail in Pending status |
| PUT | `/orders/{id}/status` | 200 detail / 404 / 409 invalid or concurrent transition |

Creation accepts only references and quantities:

```json
{
  "customerId": "<customer UUID>",
  "items": [{ "productId": "<product UUID>", "quantity": 2 }]
}
```

Select an active customer and 1–100 distinct active products, with integer quantities
1–100,000. The server reads database prices and calculates/stores totals atomically.
Client price/total fields are ignored. Detail returns line IDs, product ID/name,
quantity, unit-price snapshots, line totals and order total.

Status request example: `{"status":"Confirmed"}`. Allowed transitions are
Pending → Confirmed/Cancelled and Confirmed → Completed/Cancelled. Repeating the
current status is idempotent; terminal statuses cannot transition. There is no
order delete or general order-edit endpoint.

## Infrastructure and errors

`GET /health` checks database connectivity (200 healthy / 503 unavailable).
Validation errors use 400 ProblemDetails with field errors. Authentication uses
401, missing entities 404, reference conflicts 409 and unexpected failures a generic
500 with trace ID. Known business errors also include a code for localized UI feedback.
Response DTOs never contain password hashes. Lists are unpaginated and business data
is shared across authenticated users.
