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
| Auth | `Authorization: Bearer <accessToken>` on everything except `/auth/otp/*`, `/auth/refresh` and `/app/config`. |
| Language | The app sends `Accept-Language: en`, `si` or `ta`. Server-written text (notification titles, for example) should follow it. |
| Lists | Wrapped in an object: `{ "items": [...] }`. Paginated lists add `"nextCursor": "..."`, which is `null` on the last page; pass it back as `?cursor=`. `GET /pickups` is paged by number instead (see there). |
| Idempotency | `POST /pickups` and `POST /orders` take an `Idempotency-Key` header. A repeat with the same key and body returns the original result. |
| Pickup and order IDs | Pickup IDs are human-readable, because the UI shows them: `RT-2041`. An order has an opaque `id` and a human-readable `orderNumber`, `ORD-5121`. Everything else is an opaque string. |

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
| 401 | Missing or expired token. The app refreshes once and retries. If that fails it clears the session and returns to sign-in. |
| 402 | Payment not accepted (`PAYMENT_FAILED`) |
| 403 | Authenticated but not allowed |
| 404 | Not found, or owned by someone else |
| 409 | Business conflict (`NO_COLLECTORS_AVAILABLE`, `SLOT_UNAVAILABLE`, `INSUFFICIENT_BALANCE`, `ADDRESS_IN_USE`, `OUT_OF_STOCK`, `PRICE_CHANGED`, `VENDOR_UNAVAILABLE`, `CONTACT_IN_USE`, `ACTIVE_PICKUPS`, `PENDING_BALANCE`) |
| 422 | Validation (`VALIDATION_ERROR`, `INVALID_PREFERRED_DATES`, `BELOW_MINIMUM`). For `VALIDATION_ERROR`, `details` maps field name to message. |
| 429 | Rate limited (`RATE_LIMITED`, `OTP_TOO_MANY_ATTEMPTS`) |
| 5xx | Server fault. The app shows a generic retry state. |

Known codes: `CONTACT_IN_USE`, `ACTIVE_PICKUPS`, `PENDING_BALANCE`, `OTP_INVALID`, `OTP_EXPIRED`, `OTP_TOO_MANY_ATTEMPTS`, `RATE_LIMITED`, `VALIDATION_ERROR`,
`NO_COLLECTORS_AVAILABLE`, `INVALID_PREFERRED_DATES`, `BELOW_MINIMUM`, `SLOT_UNAVAILABLE`, `ADDRESS_REQUIRED`, `CANNOT_CANCEL`,
`DUPLICATE_REQUEST`, `INSUFFICIENT_BALANCE`, `BELOW_MINIMUM_AMOUNT`, `BANK_DETAILS_REQUIRED`,
`NOT_FOUND`, `ADDRESS_IN_USE`, `OUTSIDE_SERVICE_AREA`, `CANNOT_DELETE_SELF`, `OUT_OF_STOCK`,
`PRICE_CHANGED`, `VENDOR_UNAVAILABLE`, `PAYMENT_FAILED`.

## Auth

Each endpoint below shows the request body, then the response body.

### `POST /auth/otp/request`

```json
{ "channel": "sms", "destination": "+94771234567" }
```

```json
{ "requestId": "otp_8f2c", "expiresInSeconds": 300, "resendAfterSeconds": 60 }
```

`channel` is `sms` or `email`. Mobile destinations are E.164, and emails are trimmed and lowercase. Sign-up
and login are the same flow: the server creates the account on first verification.

The app waits `resendAfterSeconds` before offering a new code, and allows 3 new codes per sign-in attempt.
The server should enforce its own limit with 429 `RATE_LIMITED`.

### `POST /auth/otp/verify`

```json
{ "requestId": "otp_8f2c", "code": "123456" }
```

```json
{
  "accessToken": "eyJ...",
  "refreshToken": "rt_...",
  "expiresIn": 3600,
  "isNewUser": true,
  "user": {
    "id": "usr_1",
    "fullName": null,
    "mobile": "+94771234567",
    "email": null,
    "preferredLanguage": "en",
    "hasBankDetails": false,
    "createdAt": "2026-01-14T04:30:00Z"
  }
}
```

`expiresIn` is in seconds. Errors:

| Status | Code | Meaning |
| --- | --- | --- |
| 400 | `OTP_INVALID` | Wrong code. |
| 400 | `OTP_EXPIRED` | The code is too old, or `requestId` is unknown. |
| 429 | `OTP_TOO_MANY_ATTEMPTS` | 5 wrong codes on this request. A new code is needed. |

When `isNewUser` is true, or `user.fullName` is `null`, the app continues to the complete-profile step.

### `POST /auth/refresh`

```json
{ "refreshToken": "rt_..." }
```

```json
{ "accessToken": "eyJ...", "refreshToken": "rt_...", "expiresIn": 3600 }
```

Refresh tokens rotate. Any 4xx means the session is over and the app returns to sign-in. A network failure
or 5xx keeps the session, and the original request fails normally.

### `POST /auth/logout`

```json
{ "refreshToken": "rt_..." }
```

204 with no body. Revokes the refresh token. The app clears its tokens whatever the result.

## Profile

### `GET /me`

```json
{
  "id": "usr_1",
  "fullName": "Nimali Perera",
  "mobile": "+94771234567",
  "email": "nimali.perera@gmail.com",
  "preferredLanguage": "en",
  "hasBankDetails": false,
  "createdAt": "2026-01-14T04:30:00Z",
  "avatarUrl": "https://cdn.rethread.lk/avatars/usr_1/3f9a.jpg"
}
```

`fullName` is `null` until the complete-profile step is saved, and the app uses that to resume the step
after a restart. `mobile` or `email` can be `null`, but not both. `preferredLanguage` is `en`, `si` or `ta`.

The app also calls this at startup to check a stored session.

### `PUT /me`

```json
{
  "fullName": "Nimali Perera",
  "email": "nimali.perera@gmail.com",
  "preferredLanguage": "si"
}
```

`fullName` and `preferredLanguage` are required, and `email` and `mobile` are optional. A field that is left
out keeps its value. Returns the updated user, or 422 `VALIDATION_ERROR` with `details`.

Profile sends only `fullName` and `preferredLanguage`. A language change is saved on the device first and
then here, so pushes and server-written text follow it. `email` and `mobile` are only sent by the
complete-profile step; afterwards they change through the verified contact change below.

`avatarUrl` is `null` until a photo is uploaded.

### `POST /me/contact-change/request`

```json
{ "channel": "sms", "newDestination": "+94771239876" }
```

```json
{ "requestId": "cc_51d2", "expiresInSeconds": 300, "resendAfterSeconds": 60 }
```

Sends a code to the **new** mobile number (`sms`, E.164) or email (`email`, trimmed and lowercase). Nothing
changes until it is verified. The same request again is how the app resends a code. Errors: 409
`CONTACT_IN_USE` when another account has it, 422 `VALIDATION_ERROR`, 429 `RATE_LIMITED`.

### `POST /me/contact-change/verify`

```json
{ "requestId": "cc_51d2", "code": "123456" }
```

Returns the updated user, as `GET /me`. Errors are those of `POST /auth/otp/verify`: `OTP_INVALID`,
`OTP_EXPIRED` and `OTP_TOO_MANY_ATTEMPTS`.

### `POST /me/avatar`

`multipart/form-data` with one part, `file`: a square JPEG of at most 512 × 512. The app crops, shrinks and
re-encodes the photo first, which strips EXIF data, including any location. The server should still
re-encode it. Returns the updated user with the new `avatarUrl`.

### `DELETE /me/avatar`

Returns the updated user with `avatarUrl: null`.

### `DELETE /me`

```json
{ "reason": "notUseful", "otpRequestId": "otp_8f2c", "otpCode": "123456" }
```

204 with no body. Deletes the account and everything personal in it, ends every session and drops the
user's devices. Records of completed pickups, payments and orders are kept without personal details for as
long as the law requires. Required by Google Play and the App Store.

The code comes from `POST /auth/otp/request` to the account's own mobile number, or its email when it has
no mobile. `reason` is `notUseful`, `tooFewPickups`, `privacy`, `movingAway`, `otherApp` or `other`.

| Status | Code | Meaning |
| --- | --- | --- |
| 400 | `OTP_INVALID`, `OTP_EXPIRED` | The code is wrong or too old. |
| 409 | `ACTIVE_PICKUPS` | A pickup is awaiting confirmation, confirmed, en route or collected. `details.count` says how many. |
| 409 | `PENDING_BALANCE` | The wallet holds money not yet paid out. `details.balanceCents` says how much. |

## Addresses

### `GET /addresses`

```json
{
  "items": [
    {
      "id": "adr_1",
      "label": "home",
      "customLabel": null,
      "addressLine": "42/3 Galle Road",
      "unitNo": "Flat 2",
      "city": "Colombo 03",
      "landmark": "Next to the pharmacy",
      "notes": "Blue gate, ring twice",
      "lat": 6.9022,
      "lng": 79.853,
      "isDefault": true
    }
  ]
}
```

| Field | Notes |
| --- | --- |
| `label` | `home`, `work` or `other`. |
| `customLabel` | The user's own name for an `other` address, such as "Grandma's". Ignored for `home` and `work`. Optional. |
| `addressLine` | Required. Street and number. |
| `unitNo` | Apartment or house number. Optional. |
| `city` | Required. |
| `landmark`, `notes` | Optional, for whoever comes to the door. |
| `lat`, `lng` | Where the user dropped the pin. Must be inside the service area. |
| `isDefault` | Exactly one address is the default whenever there is at least one. |

The default rules: the first address saved becomes the default; saving one with `isDefault: true` (or
calling `POST /addresses/{id}/default`) makes it the only default; `isDefault: false` on the current
default is ignored, because the default can only move to another address; deleting the default makes the
oldest remaining address the default.

Errors for create and update: 422 `VALIDATION_ERROR` with `details` naming the field (`addressLine`,
`city`), and 422 `OUTSIDE_SERVICE_AREA` when the pin is outside the area served. The app checks a box
around Sri Lanka first, so it rarely sees the second one; the server's check is the real one.

The app reverse geocodes the pin with the phone's own geocoder to prefill the form. Searching by
address text (autocomplete) is not built yet; when it is, it should go through an endpoint here, such as
`GET /addresses/search?q=`, so no Places API key ships in the app.

### `POST /addresses`

```json
{
  "label": "other",
  "customLabel": "Grandma's",
  "addressLine": "12 Lighthouse Street",
  "unitNo": "2B",
  "city": "Galle",
  "landmark": "Near the fort",
  "notes": null,
  "lat": 6.0329,
  "lng": 80.2168,
  "isDefault": false
}
```

Returns 201 with the created address, as in `GET /addresses`.

### `PUT /addresses/{id}`

The same body as `POST`; it replaces the address. Returns the updated address, or 404 `NOT_FOUND`.

### `DELETE /addresses/{id}`

204. 409 `ADDRESS_IN_USE` while a pickup that isn't delivered or cancelled uses the address; the app
explains that it can be deleted once that pickup is done. 404 `NOT_FOUND` for an unknown ID.

### `POST /addresses/{id}/default`

No body. Makes the address the only default and returns it.

## Family members

Measurements are personal data. The app sends them only to these endpoints and never logs them or adds
them to analytics or crash reports; the server should treat them the same way.

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
      "dateOfBirth": null,
      "heightCm": 160,
      "chestCm": 86,
      "waistCm": 71,
      "hipCm": 94,
      "shoeSize": "38"
    }
  ]
}
```

| Field | Values |
| --- | --- |
| `relation` | `self`, `spouse`, `son`, `daughter`, `mother`, `father`, `sibling`, `other` |
| `gender` | `female`, `male`, `other` |
| `ageGroup` | `adult`, `teen`, `kid` |
| `dateOfBirth` | `YYYY-MM-DD`, optional. Sizes go by `ageGroup`. |
| `shoeSize` | EU size as a string, such as `"38"` or `"38.5"`. Optional. |

Every user has exactly one `self` member, created by the server from their name when they complete their
profile, and it comes first in the list. Nobody else can be given `relation: self`, and `self` can't be
changed to another relation.

Measurements are whole centimetres and each may be `null`; the cm/in toggle is display only. The app
checks sensible ranges for the age group before saving (adult height 120–220 cm, kid 50–170 cm, and so
on); the server should reject only impossible values, with 422 `VALIDATION_ERROR` naming the field and not
echoing the value. Sizes are not stored: they are suggested from the measurements and the size charts
(see [Catalog](#catalog)).

### `POST /family-members`

```json
{
  "name": "Kumari Perera",
  "relation": "mother",
  "gender": "female",
  "ageGroup": "adult",
  "dateOfBirth": null,
  "heightCm": 155,
  "chestCm": 96,
  "waistCm": 84,
  "hipCm": 102,
  "shoeSize": "37"
}
```

Returns 201 with the created member. 422 `VALIDATION_ERROR` for a blank name, an impossible measurement or
`relation: self`.

### `PUT /family-members/{id}`

The same body as `POST`; it replaces the member. Returns the updated member, or 404 `NOT_FOUND`.

### `DELETE /family-members/{id}`

204. 403 `CANNOT_DELETE_SELF` for the user's own profile, 404 `NOT_FOUND` for an unknown ID. Pickup items
already listed for the member keep their sizes.

## Catalog

Reference data for the itemised flow. The app caches both lists on the device and fetches them at most
once a day, falling back to the cached copy when offline.

### `GET /catalog/garment-types`

```json
{
  "items": [
    { "id": "tShirt", "name": "T-shirt", "icon": "tShirt", "avgWeightKg": 0.2, "sizeBasis": "chest" },
    { "id": "shirt", "name": "Shirt", "icon": "shirt", "avgWeightKg": 0.25, "sizeBasis": "chest" },
    { "id": "trousers", "name": "Trousers", "icon": "trousers", "avgWeightKg": 0.5, "sizeBasis": "waist" },
    { "id": "saree", "name": "Saree", "icon": "saree", "avgWeightKg": 0.6, "sizeBasis": "none" },
    { "id": "dress", "name": "Dress", "icon": "dress", "avgWeightKg": 0.4, "sizeBasis": "chest" },
    { "id": "kidsWear", "name": "Kids wear", "icon": "kidsWear", "avgWeightKg": 0.15, "sizeBasis": "height" },
    { "id": "bedsheet", "name": "Bedsheet", "icon": "bedsheet", "avgWeightKg": 0.9, "sizeBasis": "none" },
    { "id": "other", "name": "Other", "icon": "other", "avgWeightKg": 0.3, "sizeBasis": "none" }
  ]
}
```

`name` follows `Accept-Language`. `icon` is a key the app maps to its own icon set; an unknown key shows a
generic icon. `sizeBasis` is the measurement the garment is sized by: `chest`, `waist`, `height`, or `none`
for free-size items.

### `GET /catalog/size-charts`

```json
{
  "items": [
    {
      "sizeBasis": "chest",
      "ageGroup": "adult",
      "ranges": [
        { "size": "XS", "minCm": 0, "maxCm": 82 },
        { "size": "S", "minCm": 82, "maxCm": 88 },
        { "size": "M", "minCm": 88, "maxCm": 96 },
        { "size": "L", "minCm": 96, "maxCm": 104 },
        { "size": "XL", "minCm": 104, "maxCm": 112 },
        { "size": "XXL", "minCm": 112, "maxCm": 999 }
      ]
    },
    {
      "sizeBasis": "height",
      "ageGroup": "kid",
      "ranges": [
        { "size": "2–3Y", "minCm": 0, "maxCm": 98 },
        { "size": "6–7Y", "minCm": 110, "maxCm": 122 }
      ]
    }
  ]
}
```

One chart per `sizeBasis` and `ageGroup`, ranges smallest first. A range covers `minCm` up to but not
including `maxCm`. Adults and teens use letter sizes; kids use age-based sizes by height.

The app suggests a size with the same rules the server should use (`SizeSuggestionService`): pick the
chart for the garment's basis and the member's age group (kids fall back to their height chart), take the
member's measurement for that basis, and find its range. Below the first range gives the smallest size and
above the last gives the largest. A `none` basis, a missing chart or a missing measurement gives no
suggestion.

## Pickups

A customer never gives a weight. They describe the clothes (a rough piece count in bulk, or garment by
garment), offer three preferred dates, and the collector weighs at the door. Earnings come from that
weighing.

### `GET /pickups/config`

```json
{
  "minItems": 5,
  "minPieces": 5,
  "maxPieces": 300,
  "ratePerKgCents": 12000,
  "currency": "LKR",
  "preferredDatesRequired": 3
}
```

The smallest pickup a collector will come out for: `minPieces` for a bulk request, `minItems` garments for
an itemised one. `maxPieces` is the most one bulk request can hold. `ratePerKgCents` is shown so the
customer knows the rate; earnings are worked out from the collector's weighing. `preferredDatesRequired`
is how many different dates a request must offer (3).

### `GET /pickups/availability?from=2026-11-07&days=14`

```json
{
  "items": [
    {
      "date": "2026-11-07",
      "slots": [
        { "id": "morning", "label": "Morning", "start": "08:00", "end": "12:00", "available": false },
        { "id": "afternoon", "label": "Afternoon", "start": "12:00", "end": "16:00", "available": true },
        { "id": "evening", "label": "Evening", "start": "16:00", "end": "19:00", "available": true }
      ]
    }
  ]
}
```

One entry per day from `from`, for `days` days (14 by default). `start` and `end` are local `HH:mm`.
`label` follows `Accept-Language`. A day whose slots are all unavailable is shown greyed out and can't be
chosen as a preferred date.

### `POST /pickups`

Header `Idempotency-Key: <uuid>`, required. The app makes one key per draft and sends it with every
attempt, so a retry after a timeout returns the pickup the first attempt created rather than a second one.
Reusing a key with a different body returns 409 `DUPLICATE_REQUEST`.

```json
{
  "mode": "bulk",
  "pieceCount": 25,
  "bagCount": 3,
  "preferredDates": [
    { "priority": 1, "date": "2026-11-14", "slotId": "morning" },
    { "priority": 2, "date": "2026-11-15", "slotId": "afternoon" },
    { "priority": 3, "date": "2026-11-17", "slotId": "evening" }
  ],
  "addressId": "adr_1",
  "notes": "Gate code 4412",
  "fallbackToCallCentre": false
}
```

`mode` is `bulk` (send `pieceCount`, and `bagCount` if the customer gave one) or `itemised` (send `items`):

```json
{
  "mode": "itemised",
  "items": [
    { "garmentTypeId": "shirt", "familyMemberId": "fam_2", "size": "L", "condition": "good", "quantity": 4 },
    { "garmentTypeId": "saree", "familyMemberId": "fam_1", "condition": "worn", "quantity": 2 }
  ],
  "preferredDates": [
    { "priority": 1, "date": "2026-11-14", "slotId": "morning" },
    { "priority": 2, "date": "2026-11-15", "slotId": "afternoon" },
    { "priority": 3, "date": "2026-11-17", "slotId": "evening" }
  ],
  "addressId": "adr_1",
  "fallbackToCallCentre": false
}
```

Only the fields for `mode` are sent. `familyMemberId` and `size` are optional on an item. `condition` is
`good`, `worn` or `damaged`. `notes` is optional. `preferredDates` has exactly
`preferredDatesRequired` entries on different days, ranked by `priority` from 1 (first choice). There is
no weight in the request. Returns 201 with the pickup, in the shape of `GET /pickups/{id}`, with status
`awaitingConfirmation`.

| Status | Code | Meaning |
| --- | --- | --- |
| 422 | `INVALID_PREFERRED_DATES` | Not exactly 3 dates, the same day twice, priorities other than 1 to 3, or a day that isn't open for pickups. `details` has `required`. |
| 422 | `BELOW_MINIMUM` | Under `minPieces` or `minItems`, or over `maxPieces`. `details` has the limits. |
| 409 | `SLOT_UNAVAILABLE` | A chosen slot filled up or closed. `details` has its `date` and `slotId`. The app reloads availability and returns to the dates step. |
| 422 | `ADDRESS_REQUIRED` | No address, or not one of the user's. |
| 409 | `DUPLICATE_REQUEST` | The `Idempotency-Key` was used for a different request. |
| 409 | `NO_COLLECTORS_AVAILABLE` | Nobody can take any of the dates. Retrying with `fallbackToCallCentre: true` creates it for the call centre to arrange. |

`POST /pickups/estimate` has been removed: the app no longer estimates weight or earnings.

### `GET /pickups?tab=upcoming&page=1&pageSize=20`

```json
{ "items": [], "page": 1, "pageSize": 20, "hasMore": false }
```

`tab` is `upcoming` (awaiting confirmation to collected, soonest first by confirmed date or else first
choice), `completed` (delivered to the hub, newest first) or `cancelled` (newest first). Unlike the
cursor-paginated lists, this one is paged by number, 1-based. `items` holds pickups in the shape of
`GET /pickups/{id}`.

### `GET /pickups/{id}`

```json
{
  "id": "RT-1987",
  "status": "deliveredToHub",
  "mode": "bulk",
  "pieceCount": 30,
  "bagCount": 4,
  "items": [],
  "ratePerKgCents": 12000,
  "preferredDates": [
    { "priority": 1, "date": "2026-10-26", "slot": { "id": "morning", "label": "Morning", "start": "08:00", "end": "12:00" } },
    { "priority": 2, "date": "2026-10-27", "slot": { "id": "afternoon", "label": "Afternoon", "start": "12:00", "end": "16:00" } },
    { "priority": 3, "date": "2026-10-29", "slot": { "id": "evening", "label": "Evening", "start": "16:00", "end": "19:00" } }
  ],
  "confirmedDate": { "date": "2026-10-26", "slotId": "morning", "slotLabel": "Morning" },
  "weighing": {
    "weightKg": 9.5,
    "method": "manual",
    "ratePerKgCents": 12000,
    "earningsCents": 114000,
    "weighedAt": "2026-10-26T04:06:00Z"
  },
  "weightFlag": {
    "reasons": ["wetSoaked"],
    "note": "The bags were next to the garden tap and most items felt damp.",
    "photoUrls": ["https://cdn.rethread.lk/flags/RT-1987-1.jpg", "https://cdn.rethread.lk/flags/RT-1987-2.jpg"],
    "flaggedAt": "2026-10-26T04:04:00Z"
  },
  "addressId": "adr_1",
  "addressLabel": "home",
  "addressLine": "42/3 Galle Road, Colombo 03",
  "addressLocation": { "lat": 6.9022, "lng": 79.853 },
  "notes": null,
  "collector": {
    "id": "col_7",
    "name": "Ruwan Jayasinghe",
    "rating": 4.9,
    "vehicleType": "Three-wheeler",
    "vehiclePlate": "WP QA-4521",
    "phone": "+94771110000",
    "photoUrl": null
  },
  "statusHistory": [
    { "status": "awaitingConfirmation", "occurredAt": "2026-10-23T17:22:00Z" },
    { "status": "confirmed", "occurredAt": "2026-10-23T18:05:00Z" },
    { "status": "enRoute", "occurredAt": "2026-10-26T03:48:00Z" },
    { "status": "collected", "occurredAt": "2026-10-26T04:06:00Z" },
    { "status": "deliveredToHub", "occurredAt": "2026-10-26T10:06:00Z" }
  ],
  "cancelReason": null,
  "createdAt": "2026-10-23T17:22:00Z"
}
```

| Field | Notes |
| --- | --- |
| `status` | `awaitingConfirmation` → `confirmed` → `enRoute` → `collected` → `deliveredToHub`, or `cancelled`. `confirmed` means a date was confirmed and a collector assigned, together. Older values are still read: `pending` as `awaitingConfirmation` and `assigned` as `confirmed`. |
| `preferredDates` | The customer's dates, each with its slot spelled out. Any order; the app sorts by `priority`. |
| `confirmedDate` | `null` until confirmed. Usually one of `preferredDates`, but the call centre may agree another; the app then shows it on its own. |
| `collector` | `null` until confirmed. |
| `weighing` | `null` until collected. `method` is `bluetoothScale` or `manual`. A collector's reason for entering a weight by hand is internal and is not in this response. |
| `weightFlag` | `null` unless the collector noted something about the weight. `reasons` holds `wetSoaked`, `unusuallyHeavy`, `nonTextileMixed`, `dirtyContaminated` or `other`; an unknown reason reads as `other`. `note` is optional, `photoUrls` may be empty. |
| `pieceCount`, `bagCount` | Bulk only. `bagCount` is `null` if the customer didn't give one. |

There is no declared or estimated weight any more: `weightKg`, `estimatedWeightKg`,
`estimatedEarningsCents`, `actualWeightKg` and `earningsCents` at the top level, and the single `date`
and `slot`, have been removed.

### `GET /pickups/{id}/tracking`

```json
{
  "status": "enRoute",
  "collectorLocation": { "lat": 6.9112, "lng": 79.859, "updatedAt": "2026-11-06T03:52:10Z" },
  "etaMinutes": 6
}
```

Small, because the app polls it: every 15 s while the tracking screen is open, every 60 s for other active
pickups, and not at all while the app is in the background. `collectorLocation` and `etaMinutes` are
`null` unless the status is `enRoute`. A SignalR hub is expected to replace polling.

### `POST /pickups/{id}/cancel`

```json
{ "reason": "changedMind" }
```

`reason` is `changedMind`, `notReady`, `wrongDetails`, `tooLong` or `other`. Returns the updated pickup,
with `cancelReason` set. Only `awaitingConfirmation` and `confirmed` pickups can be cancelled; otherwise
409 `CANNOT_CANCEL`.

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

Every figure here is calculated on the server from the kilograms collected, using conversion factors an
admin sets per kg. The app only formats what it receives and never applies the factors itself, so a
change to a factor shows up everywhere at once.

### `GET /impact/summary?period=all`

`period` is `month` (the current calendar month), `year` (the current calendar year) or `all`.

```json
{
  "period": "all",
  "totalKg": 86.4,
  "pickupCount": 11,
  "metrics": {
    "ghgSavedKg": 302.4,
    "resourcesSavedKg": 604.8,
    "taxSavedCents": 216000,
    "forexSavedUsdCents": 6912,
    "jobsGenerated": 0.39,
    "entrepreneursCreated": 1.04,
    "clothesDonatedPieces": 62,
    "donationValueCents": 207360
  },
  "updatedAt": "2026-11-07T04:30:00Z"
}
```

| Field | Notes |
| --- | --- |
| `totalKg` | Collected weight in the period, 2 decimals. |
| `metrics.taxSavedCents` | Landfill cost the local authority no longer pays, in LKR cents. |
| `metrics.forexSavedUsdCents` | US cents. |
| `metrics.donationValueCents` | LKR cents. |
| `metrics.jobsGenerated`, `entrepreneursCreated` | Full-time equivalents, so usually fractions. Round to 2 decimals. |
| `metrics.ghgSavedKg`, `resourcesSavedKg` | kg, rounded to 1 decimal. |
| `updatedAt` | When the figures were last worked out. |

A new user gets `totalKg: 0`, `pickupCount: 0` and every metric `0`. The all-time totals must match
`impact` in `GET /home/summary`.

### `GET /impact/history?months=12`

```json
{
  "items": [
    { "month": "2025-12", "totalKg": 5.6, "ghgSavedKg": 19.6 },
    { "month": "2026-01", "totalKg": 0, "ghgSavedKg": 0 },
    { "month": "2026-11", "totalKg": 11.4, "ghgSavedKg": 39.9 }
  ]
}
```

Exactly `months` entries (default 12, at most 24), oldest first, ending with the current month. Months
without a pickup are included with zeros.

### `GET /impact/factors`

```json
{
  "factors": [
    { "key": "ghgSavedKg", "label": "GHG saved", "perKg": 3.5, "unit": "kgCo2e" },
    { "key": "resourcesSavedKg", "label": "Resources saved for next generations", "perKg": 7, "unit": "kg" },
    { "key": "taxSavedCents", "label": "Tax money saved for local authority", "perKg": 2500, "unit": "lkrCents" },
    { "key": "forexSavedUsdCents", "label": "Forex saved for country", "perKg": 80, "unit": "usdCents" },
    { "key": "jobsGenerated", "label": "Jobs generated", "perKg": 0.0045, "unit": "jobs" },
    { "key": "entrepreneursCreated", "label": "Entrepreneurs created", "perKg": 0.012, "unit": "entrepreneurs" },
    { "key": "clothesDonatedPieces", "label": "Free clothes donated to people in need", "perKg": 0.72, "unit": "pieces" },
    { "key": "donationValueCents", "label": "Total donation value", "perKg": 2400, "unit": "lkrCents" }
  ],
  "updatedAt": "2026-10-01T04:30:00Z"
}
```

| Field | Notes |
| --- | --- |
| `key` | The `metrics` field the factor produces. The app lists unknown keys with `label`. |
| `label` | Follows `Accept-Language`. The app uses its own label for keys it knows. |
| `perKg` | What 1 kg counts for. In `lkrCents` and `usdCents` it is a whole number of cents. |
| `unit` | `kgCo2e`, `kg`, `lkrCents`, `usdCents`, `jobs`, `entrepreneurs` or `pieces`. |
| `updatedAt` | When an admin last changed any factor. |

### `GET /impact/milestones`

```json
{
  "earned": [
    { "id": "ms_10kg", "title": "10 kg club", "achievedAt": "2025-11-05T04:30:00Z" },
    { "id": "ms_75kg", "title": "75 kg club", "achievedAt": "2026-09-05T04:30:00Z" }
  ],
  "next": { "id": "ms_100kg", "title": "100 kg club", "targetKg": 100, "currentKg": 86.4 }
}
```

`earned` is oldest first. `title` follows `Accept-Language`. `next` is `null` once every milestone is
earned. A new user gets `earned: []` and the first milestone with `currentKg: 0`. `currentKg` is all-time,
the same as `totalKg` for `period=all`.

## Shop

Rejuvenated garments are usually one of a kind, so `stock` is usually 1 and drops to 0 once sold. The
app shows prices but never adds up charges: every total comes from `POST /orders/quote`.

### `GET /shop/categories`

```json
{
  "items": [
    { "id": "women", "name": "Women" },
    { "id": "sarees", "name": "Sarees" },
    { "id": "home-textiles", "name": "Home textiles" }
  ],
  "sizes": ["XS", "S", "M", "L", "XL", "8–10Y", "Free size"],
  "maxPriceCents": 1000000,
  "newTodayCount": 24
}
```

The category chips, the sizes the filter sheet offers, the top of its price slider, and the count on
the "Uploaded today" banner. `name` follows `Accept-Language`.

### `GET /shop/products?search=&category=&sizes=&minPriceCents=&maxPriceCents=&condition=&gender=&sort=newest&newToday=true&page=1&pageSize=20`

```json
{
  "items": [
    {
      "id": "prd_1",
      "name": "Patchwork Denim Jacket",
      "categoryId": "women",
      "size": "M",
      "priceCents": 345000,
      "conditionGrade": "excellent",
      "gender": "women",
      "stock": 1,
      "isNewToday": true,
      "imageUrl": "https://cdn.example.com/products/prd_1/1.jpg"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "hasMore": true,
  "totalCount": 41
}
```

Every parameter is optional, and the app leaves out the ones not set.

| Parameter | Meaning |
| --- | --- |
| `search` | Matches the product name. The app sends it 400 ms after typing stops. |
| `category` | A category `id`. |
| `sizes` | Comma separated, any of them: `M,L`. |
| `minPriceCents`, `maxPriceCents` | Inclusive. |
| `condition` | Comma separated grades: `likeNew`, `excellent`, `good`, `fair`. |
| `gender` | `women`, `men`, `kids` or `unisex`. `unisex` pieces also match `women` and `men`. |
| `sort` | `newest` (default), `priceAsc` or `priceDesc`. |
| `newToday` | `true` for pieces uploaded today, Sri Lanka time. |
| `page`, `pageSize` | 1-based. The app asks for 20 at a time. |

Sold-out pieces stay in the list with `stock: 0`, so the shop can show them as sold out. `imageUrl` is
the first photo, or `null`.

### `GET /shop/products/{id}`

```json
{
  "id": "prd_1",
  "name": "Patchwork Denim Jacket",
  "categoryId": "women",
  "size": "M",
  "priceCents": 345000,
  "conditionGrade": "excellent",
  "gender": "women",
  "stock": 1,
  "isNewToday": true,
  "imageUrls": [
    "https://cdn.example.com/products/prd_1/1.jpg",
    "https://cdn.example.com/products/prd_1/2.jpg"
  ],
  "related": [
    {
      "id": "prd_3",
      "name": "Saree-silk Wrap Skirt",
      "categoryId": "women",
      "size": "S",
      "priceCents": 225000,
      "conditionGrade": "excellent",
      "gender": "women",
      "stock": 1,
      "isNewToday": false,
      "imageUrl": null
    }
  ],
  "uploadedAt": "2026-11-07T03:30:00Z",
  "description": "Cut from 4 pairs of collected jeans, re-stitched in Moratuwa.",
  "material": "100% cotton denim",
  "measurements": { "chestCm": 96, "lengthCm": 58, "waistCm": null },
  "rejuvenatedBy": "ReThread Moratuwa Studio",
  "impactKg": 0.8
}
```

`description`, `material`, `measurements`, `rejuvenatedBy` and `impactKg` are optional, and the app shows
only what is there. `measurements` are the garment's, in whole centimetres; chest and waist are full
circumferences (the flat measurement doubled), so the app can compare them with family profiles to show
"Fits: Nimali". `impactKg` is textile kept out of landfill, worked out on the server. `related` holds up to
6 products in the same category that are still for sale. An unknown ID is 404 `NOT_FOUND`.

### `GET /shop/franchise-vendors?lat=6.9022&lng=79.853`

```json
{
  "items": [
    {
      "id": "vnd_kollupitiya",
      "name": "ReThread Point Kollupitiya",
      "address": "112 Galle Road, Colombo 03",
      "lat": 6.9105,
      "lng": 79.8501,
      "openingHours": "Mon–Sat 9:00–19:00",
      "phone": "+94112345601",
      "distanceKm": 1.0
    }
  ]
}
```

Click & Collect points that are taking collections. With `lat` and `lng` (the user's chosen or default
address), each has `distanceKm` and the list is nearest first; without them `distanceKm` is `null`.
`openingHours` is display text and follows `Accept-Language`.

## Orders

The cart lives on the device, so there are no cart endpoints. The app sends the cart as `items` to the
quote and to the order.

### `POST /orders/quote`

```json
{
  "items": [
    { "productId": "prd_1", "quantity": 1 },
    { "productId": "prd_7", "quantity": 1 }
  ],
  "deliveryMethod": "clickAndCollect",
  "vendorId": "vnd_nugegoda"
}
```

```json
{
  "items": [
    {
      "productId": "prd_1",
      "name": "Patchwork Denim Jacket",
      "size": "M",
      "quantity": 1,
      "unitPriceCents": 345000,
      "lineTotalCents": 345000,
      "stock": 1,
      "available": true,
      "imageUrl": "https://cdn.example.com/products/prd_1/1.jpg"
    },
    {
      "productId": "prd_7",
      "name": "Handloom Cotton Kurta",
      "size": "M",
      "quantity": 1,
      "unitPriceCents": 265000,
      "lineTotalCents": 0,
      "stock": 0,
      "available": false,
      "imageUrl": null
    }
  ],
  "subtotalCents": 345000,
  "shippingFeeCents": null,
  "transportChargeCents": 25000,
  "discountCents": null,
  "totalCents": 370000,
  "currency": "LKR",
  "estimatedDeliveryDays": 4,
  "unavailableItemIds": ["prd_7"]
}
```

Prices the cart for a delivery method. The app calls it when the cart opens, whenever the cart or the
delivery choice changes, and again just before placing the order. Every amount here is the server's;
the app only displays them.

| Field | Notes |
| --- | --- |
| `deliveryMethod` | `homeDelivery` or `clickAndCollect`. |
| `addressId` | Optional, Home Delivery only. Sent when chosen, in case fees ever depend on it. |
| `vendorId` | Optional, Click & Collect only. An unknown or closed vendor is 409 `VENDOR_UNAVAILABLE`. |
| `items[].available` | `false` when there aren't `quantity` left. Such a line has `lineTotalCents: 0` and is left out of every total, and its ID is in `unavailableItemIds`. |
| `items[].stock` | Pieces left. The app offers a quantity stepper only when it is more than 1. |
| `shippingFeeCents` | Home Delivery only, otherwise `null`. |
| `transportChargeCents` | Click & Collect only, otherwise `null`: bringing the pieces to the vendor. The app shows it as its own "Transportation charge" line. |
| `discountCents` | Optional, positive, already taken off `totalCents`. |
| `estimatedDeliveryDays` | Days to the door, or to being ready at the vendor. |

Charges are `null` when nothing in the cart is available.

### `POST /orders`

Header `Idempotency-Key: <uuid>`, required.

```json
{
  "items": [{ "productId": "prd_1", "quantity": 1 }],
  "deliveryMethod": "homeDelivery",
  "addressId": "adr_1",
  "contactName": "Nimali Perera",
  "contactMobile": "+94771234567",
  "quoteTotalCents": 380000
}
```

```json
{ "orderId": "ord_5124a", "orderNumber": "ORD-5124", "totalCents": 380000, "paymentRequired": true }
```

`items` leaves out lines the quote marked unavailable. Send `addressId` for Home Delivery and `vendorId`
for Click & Collect. `quoteTotalCents` is the total the user saw and agreed to. The order holds its pieces
while payment is taken; it then waits for `POST /orders/{id}/payment-confirmation`. An unpaid order is
released after 30 minutes and becomes `cancelled`.

The app makes one key per checkout attempt and sends it again on a retry of the same request, such as
after a timeout, so the server returns the order the first try created. A changed request (agreeing to a
new total, say) is a new attempt with a new key. A failed or cancelled payment is retried against the
order already made, never by placing another.

| Status | Code | Meaning |
| --- | --- | --- |
| 409 | `OUT_OF_STOCK` | Something sold out. `details.productIds` lists them. The app flags them in the cart, quotes again and asks the user to place the order again. |
| 409 | `PRICE_CHANGED` | The server's total isn't `quoteTotalCents`. `details.totalCents` has the new one. The app quotes again and asks the user to confirm. |
| 409 | `VENDOR_UNAVAILABLE` | The vendor stopped taking collections. |
| 422 | `ADDRESS_REQUIRED` | Home Delivery with no address, or not one of the user's. |
| 409 | `DUPLICATE_REQUEST` | The `Idempotency-Key` was used for a different request. |

When an order is placed, the backend tells the call centre so they can arrange delivery or transport to
the vendor. The app does nothing more.

### `POST /orders/{id}/payment-confirmation`

```json
{ "status": "succeeded", "paymentReference": "pay_7f3a9c" }
```

The app sends this when the payment gateway returns. `status` is `succeeded`, `failed`, `cancelled` (the
user closed the payment page; `paymentReference` is left out) or `pending` (the gateway will settle
later). The server checks a reference with the gateway before marking the order paid, because the app's
word alone isn't proof of payment; the gateway's own notification to the server is the final say. Returns
the order, as in `GET /orders/{id}`.

`succeeded` for a payment the gateway has no record of is 402 `PAYMENT_FAILED`. The app then lets the
user pay again for the same order.

### `GET /orders?page=1&pageSize=20`

```json
{ "items": [], "page": 1, "pageSize": 20, "hasMore": false }
```

Newest first, paged by number like `GET /pickups`. `items` holds orders in the shape of
`GET /orders/{id}`.

### `GET /orders/{id}`

```json
{
  "id": "ord_5124a",
  "orderNumber": "ORD-5124",
  "status": "confirmed",
  "deliveryMethod": "homeDelivery",
  "items": [
    {
      "productId": "prd_1",
      "name": "Patchwork Denim Jacket",
      "size": "M",
      "quantity": 1,
      "unitPriceCents": 345000,
      "imageUrl": "https://cdn.example.com/products/prd_1/1.jpg"
    }
  ],
  "subtotalCents": 345000,
  "shippingFeeCents": 35000,
  "transportChargeCents": null,
  "discountCents": null,
  "totalCents": 380000,
  "currency": "LKR",
  "contactName": "Nimali Perera",
  "contactMobile": "+94771234567",
  "paymentStatus": "paid",
  "statusHistory": [
    { "status": "placed", "occurredAt": "2026-11-07T04:11:00Z" },
    { "status": "confirmed", "occurredAt": "2026-11-07T04:30:00Z" }
  ],
  "address": { "id": "adr_1", "line": "42/3 Galle Road, Colombo 03" },
  "vendor": null,
  "estimatedDeliveryDate": "2026-11-10",
  "createdAt": "2026-11-07T04:11:00Z"
}
```

| Field | Notes |
| --- | --- |
| `id` | Opaque, for URLs. `orderNumber` is what the UI shows. |
| `status` | Home Delivery: `placed` → `confirmed` → `dispatched` → `outForDelivery` → `delivered`. Click & Collect: `placed` → `confirmed` → `inTransitToVendor` → `readyForCollection` → `collected`. Either can become `cancelled`. |
| `statusHistory` | The statuses reached so far, oldest first. |
| `paymentStatus` | `pending`, `paid` or `failed`. |
| `address` | Home Delivery only, as it was when ordered. |
| `vendor` | Click & Collect only: `{ id, name, address, openingHours, phone }`. |
| `estimatedDeliveryDate` | `YYYY-MM-DD`, or `null` once cancelled. |

## Notifications

### `GET /notifications?page=1&pageSize=20`

Newest first, paged by number like `GET /pickups`.

```json
{
  "items": [
    {
      "id": "ntf_1",
      "type": "pickupStatus",
      "title": "Ruwan is en route",
      "body": "Your collector is ~6 min from 42/3 Galle Road.",
      "createdAt": "2026-11-06T04:06:00Z",
      "readAt": null,
      "targetId": "RT-2041",
      "pickupStatus": "enRoute"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "hasMore": true
}
```

`title` and `body` are written by the server in the user's language (`Accept-Language`). `type` decides
the icon and where a tap goes, with `targetId`:

| `type` | `targetId` | A tap opens |
| --- | --- | --- |
| `pickupStatus` | Pickup ID | Pickup tracking. `pickupStatus` is `enRoute`, `collected` (weighed, with the weight and earnings in `body`), `deliveredToHub` or `cancelled`. |
| `pickupDateConfirmed` | Pickup ID | Pickup tracking. Sent when a preferred date is confirmed and a collector assigned: "Your pickup is confirmed for Sat, 15 Nov · Morning." |
| `weightFlagged` | Pickup ID | Pickup tracking, where the collector's note shows. "Your collector added a note about your pickup weight." |
| `paymentCredited` | Pickup ID that earned it | Pickup tracking, until the wallet is built |
| `orderUpdate` | Order `id` (not `orderNumber`) | Order detail |
| `milestone` | `null` | My impact |
| `promotion` | Product ID, or `null` | Product detail, or nothing |
| `system` | `null` | Nothing |

`pickupStatus` is `null` for other types. An unknown `type` shows as `system`, so new types can be added
without breaking older apps. A missing `targetId` opens nothing. `weightUpdated`, which compared a declared
weight with the collector's, has been removed along with declared weights. Push data may spell types in
snake_case (`pickup_date_confirmed`, `weight_flagged`); the app normalises them.

### `GET /notifications/unread-count`

```json
{ "unreadCount": 3 }
```

For the Home bell. The app asks on startup, when it returns to the foreground, when a push arrives and
when a pickup changes status.

### `POST /notifications/{id}/read`

204. Reading an already-read notification is fine.

### `POST /notifications/read-all`

204.

### `DELETE /notifications/{id}`

204. The app waits about four seconds before calling this, while it offers Undo.

### `GET /notifications/preferences`

```json
{ "pickupUpdates": true, "orders": true, "payments": true, "offers": false }
```

### `PUT /notifications/preferences`

```json
{ "pickupUpdates": true, "orders": false, "payments": true, "offers": false }
```

Returns the saved preferences. `pickupUpdates` is always `true`: the user must hear when a collector is on
the way. The server ignores `false` for it and returns `true`. The other three switch off both the push
and the list entry for that category: `orders` covers `orderUpdate`, `payments` covers
`paymentCredited`, and `offers` covers `promotion`. `milestone` and `system` always go out.

## Devices

Where to send push notifications. See `docs/NOTIFICATIONS.md` for the payload the backend sends.

### `POST /devices`

```json
{ "token": "fcm-registration-token", "platform": "android", "appVersion": "0.1.0", "locale": "si" }
```

204. Called after sign-in, at startup with a stored session, and whenever Firebase issues a new token.
`token` is the FCM registration token. Upsert by token: a token moving to another user (one phone, two
accounts) belongs to the latest. `platform` is `android` or `ios`, and `locale` is `en`, `si` or `ta`.

The app never logs the token.

### `DELETE /devices/{token}`

204, also when the token is unknown. Called on sign-out before `POST /auth/logout`. When the session
expires instead, the app can't call this, so the server should drop a user's devices when it revokes
their refresh tokens, and drop tokens FCM reports as unregistered.

## Support

### `GET /support/faqs`

```json
{
  "items": [
    {
      "id": "faq_1",
      "category": "pickups",
      "question": "What can I give for recycling?",
      "answer": "Any clothes and household textiles, even torn or stained."
    }
  ]
}
```

`category` is `pickups`, `payments`, `shop` or `account`. Text follows `Accept-Language`, in display order.

### `POST /support/tickets`

`multipart/form-data`:

| Part | Notes |
| --- | --- |
| `category` | `pickup`, `pickupWeight`, `payment`, `order`, `app`, `account` or `other` |
| `pickupId` | Optional. The pickup the ticket is about, such as `RT-1987`. Sent when the form is opened from a collector's weight note, with `category` `pickupWeight`. |
| `message` | At least 10 characters |
| `screenshot` | Optional JPEG, at most 1600 px on its longest side, EXIF already removed |

```json
{ "id": "tck_1042", "reference": "TCK-1042", "createdAt": "2026-11-06T04:06:00Z" }
```

`reference` is shown to the user to quote on the phone. 422 `VALIDATION_ERROR` for a short message.

## App config

### `GET /app/config`

Read at startup without authentication and cached on the device, so it can change without a release.

```json
{
  "support": {
    "phone": "+94117544444",
    "whatsapp": "+94771234500",
    "email": "support@rethread.lk"
  },
  "legal": {
    "termsUrl": "https://rethread.lk/terms",
    "privacyUrl": "https://rethread.lk/privacy"
  },
  "storeUrls": {
    "android": "https://play.google.com/store/apps/details?id=com.morenta.pctw.user",
    "ios": "https://apps.apple.com/lk/app/rethread/id0000000000"
  },
  "features": { "walletEnabled": false, "pushEnabled": null },
  "minSupportedVersion": "0.1.0",
  "latestVersion": "0.2.0"
}
```

| Field | Notes |
| --- | --- |
| `support.whatsapp`, `support.email` | Optional. `null` hides the option. |
| `features.walletEnabled` | Wallet, redeem and bank details. `false` until their rules are final. |
| `features.pushEnabled` | `null` keeps the build's `PUSH_ENABLED`; `true` or `false` overrides it. |
| `minSupportedVersion` | An older app shows a blocking "Update required" screen. |
| `latestVersion` | An older app offers the update once per version, on Home. |

Versions are compared part by part as numbers (`0.10.0` is newer than `0.9.2`), ignoring anything after `-`
or `+`. Until the first config loads, the app uses built-in defaults with nothing blocked and every flag off.

## Home

### `GET /home/summary`

Everything the Home tab shows, in one call, so it loads fast and can be cached whole for offline use.

```json
{
  "user": { "firstName": "Nimali", "lastName": "Perera" },
  "wallet": {
    "availableBalanceCents": 486000,
    "pendingBalanceCents": 0,
    "currency": "LKR",
    "ratePerKgCents": 12000
  },
  "activePickups": [
    {
      "id": "RT-2041",
      "status": "enRoute",
      "pieceCount": 25,
      "itemCount": null,
      "preferredDates": [
        { "priority": 1, "date": "2026-11-07", "slot": { "id": "morning", "label": "Morning", "start": "08:00", "end": "12:00" } },
        { "priority": 2, "date": "2026-11-08", "slot": { "id": "afternoon", "label": "Afternoon", "start": "12:00", "end": "16:00" } },
        { "priority": 3, "date": "2026-11-10", "slot": { "id": "evening", "label": "Evening", "start": "16:00", "end": "19:00" } }
      ],
      "confirmedDate": { "date": "2026-11-07", "slotId": "morning", "slotLabel": "Morning" },
      "collector": { "name": "Ruwan Jayasinghe", "vehicleType": "Three-wheeler", "photoUrl": null }
    }
  ],
  "activePickupCount": 2,
  "impact": {
    "totalWeightKg": 86.4,
    "ghgSavedKg": 302.4,
    "garmentsDonated": 62,
    "totalDonationValueCents": 207360
  },
  "milestone": { "id": "ms_75kg", "title": "You've recycled 75 kg!", "message": "Welcome to the 75 kg club, Nimali." },
  "newProducts": [
    { "id": "prd_1", "name": "Patchwork Denim Jacket", "size": "M", "priceCents": 345000, "imageUrl": null }
  ],
  "unreadNotificationCount": 2
}
```

| Field | Notes |
| --- | --- |
| `user.lastName` | `null` if the user gave one name. Used for the avatar initials. |
| `wallet` | Same values as `GET /wallet`. `pendingBalanceCents` is earnings not yet credited. |
| `activePickups` | Pickups with status `awaitingConfirmation`, `confirmed`, `enRoute` or `collected`, soonest first (by confirmed date, or else first choice), at most 3. `pieceCount` is set for bulk and `itemCount` (garments) for itemised. `preferredDates` and `confirmedDate` have the shapes in `GET /pickups/{id}`; `confirmedDate` is `null` while awaiting confirmation. `collector` is `null` until confirmed. There is no declared weight. |
| `activePickupCount` | All active pickups, which can be more than the list holds. |
| `impact` | The all-time totals from `GET /impact/summary?period=all`: `totalWeightKg` is `totalKg`, `garmentsDonated` is `clothesDonatedPieces`, `totalDonationValueCents` is `donationValueCents`. All zero for a new user. |
| `milestone` | The latest milestone reached, or `null`. `title` and `message` follow `Accept-Language`. The app hides one the user has dismissed by `id`. |
| `newProducts` | Up to 6, newest first. `size` is the product's default size. `imageUrl` is `null` when there is no photo. |

A new user gets zero balances, empty lists, zero impact and `milestone: null`.

## Not in this contract yet

- Chat with the collector. The app uses SMS for now. Live location is polled from
  `GET /pickups/{id}/tracking` until a SignalR hub exists.
- The payment gateway. Until one is chosen the app uses a mock gateway and reports its result to
  `POST /orders/{id}/payment-confirmation`; the real gateway's server-to-server notification will be
  added with it.
- Paying for orders from the wallet balance. Waiting for the wallet and redeem rules to be final.
- Reverse geocoding for the location pin, when the Addresses screens are built.
