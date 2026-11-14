# ReThread API contract (user app)

What the Flutter user app expects from the ASP.NET Core API. The backend does not exist yet, so this
document is the agreement both sides build against. The DTOs in `lib/features/*/data/models/` mirror it
field for field, and the mock sources return the same shapes, which is why switching from mock to real
needs no UI changes.

If you change a shape here, change the DTO and the mock source in the same PR.

> Later we may generate the Dart client from the .NET OpenAPI (Swagger) spec. When that spec exists, it
> replaces this file as the source of truth, and the generated client slots in behind the
> `<feature>_remote_source.dart` classes. Nothing above the source layer should need to change.

## Conventions

| Topic | Rule |
| --- | --- |
| Base path | Everything is under `/api/v1`. `API_BASE_URL` in `config/*.json` includes the prefix, so paths below are relative to it. |
| JSON | camelCase keys. Enums are camelCase strings (`enRoute`, `clickAndCollect`). |
| Dates | ISO-8601 in UTC with a `Z` suffix: `2026-11-06T03:18:00Z`. Date-only values are `YYYY-MM-DD`. |
| Money | Integer LKR cents, in fields ending in `Cents`. `486000` is Rs. 4,860.00. Never a float. Signed where a field says so. |
| Weight | Kilograms as a JSON number with at most 2 decimals, in fields ending in `Kg`. |
| Auth | `Authorization: Bearer <accessToken>` on everything except `/auth/otp/*` and `/auth/refresh`. |
| Language | The app sends `Accept-Language: en`, `si` or `ta`. Server-written text (notification titles, for example) should follow it. |
| Lists | Wrapped in an object: `{ "items": [...] }`. Paginated lists add `"nextCursor": "..."`, which is `null` on the last page; pass it back as `?cursor=`. |
| Pickup and order IDs | Human-readable, because the UI shows them: `RT-2041`, `ORD-5121`. Everything else is an opaque string. |

### Errors

Any non-2xx response has this body:

```json
{
  "code": "INSUFFICIENT_BALANCE",
  "message": "Amount is more than your balance.",
  "details": { "balanceCents": 486000 }
}
```

`code` is a stable `UPPER_SNAKE_CASE` string the app can branch on. `message` is for logs and as a
fallback; the app shows its own localized text wherever it recognises the failure. `details` is optional.

| Status | Used for |
| --- | --- |
| 400 | Malformed request |
| 401 | Missing or expired token. The app clears the session and returns to sign-in. |
| 403 | Authenticated but not allowed |
| 404 | Not found, or owned by someone else |
| 409 | Business conflict (`NO_COLLECTORS_AVAILABLE`, `INSUFFICIENT_BALANCE`) |
| 422 | Validation. `details` maps field name to message. |
| 429 | Rate limited (OTP requests in particular) |
| 5xx | Server fault. The app shows a generic retry state. |

Known codes: `INVALID_OTP`, `OTP_EXPIRED`, `OTP_RATE_LIMITED`, `NO_COLLECTORS_AVAILABLE`,
`INSUFFICIENT_BALANCE`, `BELOW_MINIMUM_AMOUNT`, `BANK_DETAILS_REQUIRED`, `PRODUCT_UNAVAILABLE`,
`PICKUP_NOT_CANCELLABLE`.

## Auth

### `POST /auth/otp/request`

```json
// request
{ "channel": "mobile", "destination": "+94771234567" }
// 200
{ "challengeId": "otp_8f2c", "expiresAt": "2026-11-06T03:23:00Z", "resendAfterSeconds": 30 }
```

`channel` is `mobile` or `email`. Mobile destinations are E.164. Sign-up and login are the same flow:
the server creates the account on first verification.

### `POST /auth/otp/verify`

```json
// request
{ "challengeId": "otp_8f2c", "code": "123456" }
// 200
{
  "accessToken": "eyJ...",
  "refreshToken": "rt_...",
  "expiresAt": "2026-11-06T04:18:00Z",
  "isNewUser": true,
  "user": { "...": "see GET /me" }
}
```

Errors: 400 `INVALID_OTP`, 400 `OTP_EXPIRED`. When `isNewUser` is true, the app continues to the name and
location steps.

### `POST /auth/refresh`

```json
// request
{ "refreshToken": "rt_..." }
// 200: same token fields as /auth/otp/verify, without user or isNewUser
{ "accessToken": "eyJ...", "refreshToken": "rt_...", "expiresAt": "2026-11-06T05:18:00Z" }
```

### `POST /auth/logout`

Empty body, 204. Revokes the refresh token.

## Profile

### `GET /me`

```json
{
  "id": "usr_1",
  "firstName": "Nimali",
  "lastName": "Perera",
  "mobile": "+94771234567",
  "email": "nimali.perera@gmail.com",
  "language": "en",
  "hasBankDetails": false,
  "createdAt": "2026-01-14T04:30:00Z"
}
```

`firstName`, `lastName`, `mobile` and `email` can be `null` while sign-up is incomplete. `language` is
`en`, `si` or `ta`.

### `PATCH /me`

```json
// request: any subset
{ "firstName": "Nimali", "lastName": "Perera", "language": "si" }
```

Returns the updated user.

## Addresses

### `GET /addresses`

```json
{
  "items": [
    {
      "id": "adr_1",
      "label": "home",
      "line": "42/3 Galle Road, Colombo 03",
      "latitude": 6.9147,
      "longitude": 79.8497,
      "isDefault": true
    }
  ]
}
```

`label` is `home`, `work` or `other`.

### `POST /addresses`

Body is the address without `id`. Returns 201 with the created address.

### `PATCH /addresses/{id}`

Any subset of the fields. Returns the updated address.

### `DELETE /addresses/{id}`

204.

## Family members

### `GET /family-members`

```json
{
  "items": [
    {
      "id": "fam_1",
      "name": "Nimali Perera",
      "relation": "self",
      "gender": "female",
      "ageGroup": "adult",
      "heightCm": 160,
      "chestCm": 86,
      "waistCm": 71,
      "hipCm": 94,
      "shoeSize": "38",
      "topSize": "M",
      "bottomSize": "28"
    }
  ]
}
```

| Field | Values |
| --- | --- |
| `relation` | `self`, `spouse`, `son`, `daughter`, `mother`, `father`, `sibling`, `other` |
| `gender` | `female`, `male`, `other` |
| `ageGroup` | `toddler`, `child`, `teen`, `adult`, `senior` |

Measurements are always stored in centimetres; the cm/in toggle is display only. `topSize` and
`bottomSize` are derived by the server from the measurements, so they are ignored on write.

### `POST /family-members`

Body is the member without `id`, `topSize` or `bottomSize`. Returns 201 with the created member.

### `PATCH /family-members/{id}`

Any subset. Returns the updated member.

### `DELETE /family-members/{id}`

204.

## Pickups

### `GET /pickups/garment-types`

Reference data for the itemised flow and its weight estimate.

```json
{
  "items": [
    { "id": "tShirt", "estimatedKg": 0.2 },
    { "id": "shirt", "estimatedKg": 0.25 },
    { "id": "trousers", "estimatedKg": 0.5 },
    { "id": "saree", "estimatedKg": 0.6 },
    { "id": "dress", "estimatedKg": 0.4 },
    { "id": "kidsWear", "estimatedKg": 0.15 },
    { "id": "bedsheet", "estimatedKg": 0.9 },
    { "id": "other", "estimatedKg": 0.3 }
  ]
}
```

### `GET /pickups?group=upcoming&cursor=`

`group` is `upcoming`, `completed` or `cancelled`. Returns `{ "items": [Pickup], "nextCursor": null }`.

### `POST /pickups`

```json
// request
{
  "method": "bulk",
  "bulkMode": "weight",
  "declaredWeightKg": 8.0,
  "declaredPieces": null,
  "items": [],
  "scheduledDate": "2026-11-07",
  "slot": "morning",
  "addressId": "adr_1",
  "notes": "Gate code 4412",
  "fallbackToCallCentre": false
}
```

Itemised example, with `method: "itemised"` and the bulk fields `null`:

```json
{ "garmentType": "shirt", "memberId": "fam_2", "size": "M", "condition": "good", "quantity": 3 }
```

| Field | Values |
| --- | --- |
| `method` | `bulk`, `itemised` |
| `bulkMode` | `weight`, `pieces` (`null` when itemised) |
| `slot` | `morning` (8 AM to 12 PM), `afternoon` (12 to 4 PM), `evening` (4 to 7 PM) |
| `condition` | `good`, `worn`, `damaged` |

Returns 201 with the created pickup. If nobody can take it, returns 409 `NO_COLLECTORS_AVAILABLE` and the
app shows the "no collectors" screen. Retrying with `fallbackToCallCentre: true` creates the pickup with
status `pending` for the call centre to assign manually.

### `GET /pickups/{id}`

```json
{
  "id": "RT-2041",
  "status": "enRoute",
  "method": "bulk",
  "bulkMode": "weight",
  "declaredWeightKg": 8.0,
  "declaredPieces": null,
  "estimatedWeightKg": 8.0,
  "actualWeightKg": null,
  "ratePerKgCents": 12000,
  "earningsCents": null,
  "scheduledDate": "2026-11-06",
  "slot": "morning",
  "addressId": "adr_1",
  "addressLine": "42/3 Galle Road, Colombo 03",
  "notes": null,
  "items": [],
  "collector": {
    "id": "col_7",
    "name": "Ruwan Jayasinghe",
    "rating": 4.9,
    "vehicleType": "Three-wheeler",
    "vehiclePlate": "WP QA-4521",
    "phone": "+94771110000",
    "photoUrl": null
  },
  "etaMinutes": 6,
  "timeline": [
    { "status": "pending", "occurredAt": "2026-11-06T03:22:00Z", "expectedAt": null },
    { "status": "assigned", "occurredAt": "2026-11-06T03:35:00Z", "expectedAt": null },
    { "status": "enRoute", "occurredAt": "2026-11-06T03:48:00Z", "expectedAt": null },
    { "status": "collected", "occurredAt": null, "expectedAt": null },
    { "status": "deliveredToHub", "occurredAt": null, "expectedAt": "2026-11-06T10:30:00Z" }
  ],
  "createdAt": "2026-11-06T03:22:00Z"
}
```

`status` is `pending`, `assigned`, `enRoute`, `collected`, `deliveredToHub` or `cancelled`.
`collector` is `null` until assigned. `actualWeightKg` and `earningsCents` are `null` until collection.

### `POST /pickups/{id}/cancel`

Empty body. Returns the updated pickup. 409 `PICKUP_NOT_CANCELLABLE` once the pickup is collected.

## Wallet

### `GET /wallet`

```json
{ "balanceCents": 486000, "pendingCents": 0, "ratePerKgCents": 12000, "currency": "LKR" }
```

### `GET /wallet/transactions?cursor=`

```json
{
  "items": [
    {
      "id": "txn_1",
      "type": "pickupEarning",
      "status": "completed",
      "title": "Pickup #RT-1987",
      "amountCents": 114000,
      "occurredAt": "2026-10-26T05:00:00Z",
      "pickupId": "RT-1987",
      "weightKg": 9.5,
      "ratePerKgCents": 12000
    }
  ],
  "nextCursor": null
}
```

`type` is `pickupEarning`, `bankTransfer`, `voucher`, `donation` or `shopOrder`. `status` is `pending`,
`completed` or `failed`. `amountCents` is signed: credits positive, debits negative. `pickupId`,
`weightKg` and `ratePerKgCents` are `null` for anything but a pickup earning.

### `POST /wallet/redemptions`

```json
// cash
{ "type": "cash", "amountCents": 200000 }
// voucher
{ "type": "voucher", "amountCents": 100000, "voucherBrandId": "vb_lanka_fresh" }
// donation
{ "type": "donation", "amountCents": 50000, "projectId": "dp_monaragala" }
// 201
{
  "id": "red_9",
  "type": "voucher",
  "amountCents": 100000,
  "status": "completed",
  "balanceCents": 386000,
  "voucherCode": "LF-7KQ2-4821"
}
```

Minimums: cash Rs. 500, donation Rs. 50. Vouchers come in the values listed by the brand. `voucherCode`
is `null` for other types. The app renders the code as a QR locally.

Errors: 409 `INSUFFICIENT_BALANCE`, 422 `BELOW_MINIMUM_AMOUNT`, 409 `BANK_DETAILS_REQUIRED` (cash only).

### `GET /wallet/voucher-brands`

```json
{
  "items": [
    {
      "id": "vb_lanka_fresh",
      "name": "Lanka Fresh Supermarkets",
      "description": "Groceries & household essentials",
      "valuesCents": [50000, 100000, 200000]
    }
  ]
}
```

### `GET /wallet/donation-projects`

```json
{
  "items": [
    {
      "id": "dp_monaragala",
      "name": "School uniforms for Monaragala kids",
      "summary": "Education · 420 children",
      "raisedCents": 34000000,
      "goalCents": 50000000
    }
  ]
}
```

### `GET /wallet/bank-details`

```json
{
  "bankName": "Commercial Bank",
  "branch": "Kollupitiya",
  "accountName": "Nimali Perera",
  "accountNumberMasked": "•••• 4821"
}
```

404 if none are saved. The full account number is never returned.

### `PUT /wallet/bank-details`

```json
// request
{
  "bankName": "Commercial Bank",
  "branch": "Kollupitiya",
  "accountName": "Nimali Perera",
  "accountNumber": "8001234821"
}
```

Returns the masked form above. The app does not store the account number on the device.

### `DELETE /wallet/bank-details`

204.

## Impact

### `GET /impact`

```json
{
  "totalWeightKg": 52.6,
  "pickupCount": 6,
  "metrics": [
    { "key": "ghgSaved", "value": 184, "unit": "kgCo2e", "series": [8, 18, 13, 25, 17, 23] },
    { "key": "resourcesSaved", "value": 368, "unit": "kg", "series": [16, 36, 26, 50, 34, 46] },
    { "key": "landfillCostSaved", "value": 131500, "unit": "lkrCents", "series": [6, 13, 9, 18, 12, 16] },
    { "key": "forexSaved", "value": 4210, "unit": "usdCents", "series": [3, 8, 5, 10, 7, 9] },
    { "key": "jobsGenerated", "value": 14, "unit": "workDays", "series": [1, 3, 2, 4, 2, 2] },
    { "key": "entrepreneursCreated", "value": 1, "unit": "microBusinesses", "series": [0, 0, 1, 1, 1, 1] },
    { "key": "garmentsDonated", "value": 38, "unit": "garments", "series": [3, 7, 5, 9, 6, 8] },
    { "key": "donationValue", "value": 125000, "unit": "lkrCents", "series": [0, 5, 2, 7, 3, 6] }
  ],
  "monthlyWeights": [
    { "month": "2026-06", "weightKg": 4.2 },
    { "month": "2026-07", "weightKg": 9.8 }
  ]
}
```

`series` is six monthly values for the sparkline, oldest first. Values in a `lkrCents` or `usdCents` unit
are integers, like all other money.

## Shop

### `GET /products?category=&maxPriceCents=&sort=newest&cursor=`

`category` is `women`, `men`, `kids` or `accessories` (omit for all). `sort` is `newest`, `priceAsc` or
`priceDesc`. Returns `{ "items": [Product], "nextCursor": null }`.

### `GET /products/{id}`

```json
{
  "id": "prd_1",
  "name": "Patchwork Denim Jacket",
  "category": "women",
  "priceCents": 345000,
  "defaultSize": "M",
  "sizes": ["XS", "S", "M", "L", "XL"],
  "story": "Cut from 4 pairs of collected jeans, re-stitched in Moratuwa.",
  "imageUrls": [],
  "isAvailable": true
}
```

## Orders

The cart lives on the device until checkout, so there are no cart endpoints.

### `GET /fulfilment-options`

```json
{
  "homeDeliveryChargeCents": 35000,
  "clickAndCollectChargeCents": 25000,
  "vendors": [
    {
      "id": "vnd_nugegoda",
      "name": "ReThread Point – Nugegoda",
      "address": "88 High Level Rd, Nugegoda",
      "distanceKm": 4.2
    }
  ]
}
```

### `POST /orders`

```json
// request
{
  "items": [{ "productId": "prd_1", "size": "M", "quantity": 1 }],
  "fulfilmentType": "homeDelivery",
  "addressId": "adr_1",
  "vendorId": null,
  "paymentMethod": "wallet"
}
```

`fulfilmentType` is `homeDelivery` (needs `addressId`) or `clickAndCollect` (needs `vendorId`).
`paymentMethod` is `card` or `wallet`.

Returns 201 with the order. Errors: 409 `PRODUCT_UNAVAILABLE`, 409 `INSUFFICIENT_BALANCE`.

### `GET /orders?cursor=`

Returns `{ "items": [Order], "nextCursor": null }`, newest first.

### `GET /orders/{id}`

```json
{
  "id": "ORD-5121",
  "status": "confirmed",
  "items": [
    { "productId": "prd_1", "name": "Patchwork Denim Jacket", "size": "M", "quantity": 1, "unitPriceCents": 345000 }
  ],
  "subtotalCents": 345000,
  "transportChargeCents": 35000,
  "totalCents": 380000,
  "fulfilmentType": "homeDelivery",
  "destination": "42/3 Galle Road, Colombo 03",
  "paymentMethod": "wallet",
  "createdAt": "2026-11-06T04:11:00Z",
  "estimatedAt": "2026-11-09T12:30:00Z"
}
```

`status` is `placed`, `confirmed`, `outForDelivery`, `readyAtVendor`, `completed` or `cancelled`.

## Notifications

### `GET /notifications?cursor=`

```json
{
  "items": [
    {
      "id": "ntf_1",
      "type": "weightUpdated",
      "title": "Weight updated by collector",
      "body": "Declared 8.0 kg → Actual 7.4 kg. Earnings Rs. 888.00.",
      "createdAt": "2026-11-06T04:06:00Z",
      "readAt": null,
      "targetType": "pickup",
      "targetId": "RT-2041"
    }
  ],
  "unreadCount": 2,
  "nextCursor": null
}
```

`type` is `weightUpdated`, `collectorEnRoute`, `hubArrival`, `orderUpdate`, `paymentCredited` or
`donationThanks`. `targetType` is `pickup`, `order`, `wallet` or `null`, and decides where a tap goes.

### `POST /notifications/{id}/read`

204.

### `POST /notifications/read-all`

204.

## Not in this contract yet

- Push notifications. Needs Firebase and a `POST /devices` endpoint to register tokens.
- Live collector location and chat on the tracking screen. Likely SignalR rather than polling.
- Card payments. `paymentMethod: "card"` needs a gateway decision first.
- Reverse geocoding for the location pin. Comes with Google Maps.
