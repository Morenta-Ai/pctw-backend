# ReThread API contract (collector app)

What the Flutter collector app expects from the ASP.NET Core API. The backend does not exist yet, so this
document is the agreement both sides build against. The DTOs in `lib/features/*/data/models/` mirror it
field for field, and the mock sources return the same shapes, which is why switching from mock to real
needs no UI changes.

The collector API shares a server, conventions and error format with the user app's contract in
[pctw-user](https://github.com/Morenta-Ai/pctw-user/blob/main/docs/API_CONTRACT.md). A pickup is one record
on the server: the customer sees it as a pickup and the collector sees it as a job.

If you change a shape here, change the DTO and the mock source in the same PR.
`test/contract/api_contract_test.dart` parses each response sample below through its DTO.

## Conventions

| Topic | Rule |
| --- | --- |
| Base path | Everything is under `/api/v1`. `API_BASE_URL` in `config/*.json` includes the prefix, so paths below are relative to it. |
| JSON | camelCase keys. Enums are camelCase strings (`onTheWay`, `callCentre`). |
| Dates | ISO-8601 in UTC with a `Z` suffix: `2026-11-07T04:30:00Z`. The app shows them in local time (UTC+05:30), 24-hour. Date-only values are `YYYY-MM-DD`. |
| Money | Integer LKR cents, in fields ending in `Cents`. `88800` is Rs. 888.00. Never a float. |
| Weight | Kilograms as a JSON number with at most 2 decimals, in fields ending in `Kg`. |
| Distance | Kilometres with 1 decimal, in fields ending in `Km`, measured from the collector's last reported position. |
| Auth | `Authorization: Bearer <accessToken>` on everything except `/auth/otp/*` and `/auth/refresh`. |
| Language | The app sends `Accept-Language: en`, `si` or `ta`. Server-written text (notification titles, for example) should follow it. |
| Lists | Wrapped in an object: `{ "items": [...] }`. Paginated lists add `"nextCursor": "..."`, which is `null` on the last page; pass it back as `?cursor=`. |
| IDs | Job IDs are human-readable because the UI shows them: `RT-24817`. Collector codes look like `COL-0457`. Receipts look like `HUB-PLG-110426-0457`. Everything else is an opaque string. |

### Errors

Any non-2xx response has this body:

```json
{
  "code": "JOB_TAKEN",
  "message": "Another collector accepted this job.",
  "details": { "jobId": "RT-24835" }
}
```

`code` is a stable `UPPER_SNAKE_CASE` string the app can branch on. `message` is for logs and as a
fallback; the app shows its own localized text wherever it recognises the failure. `details` is optional.

| Status | Used for |
| --- | --- |
| 400 | Malformed request, wrong or expired code |
| 401 | Missing or expired token. The app clears the session and returns to login. |
| 403 | Authenticated but not allowed, such as acting on another collector's job |
| 404 | Not found |
| 409 | Business conflict (`JOB_TAKEN`, `ASSIGNMENT_EXPIRED`, `LOAD_FULL`) |
| 422 | Validation. `details` maps field name to message. |
| 429 | Rate limited (code requests in particular) |
| 5xx | Server fault. The app shows a generic retry state. |

Known codes: `COLLECTOR_NOT_FOUND`, `INVALID_OTP`, `OTP_EXPIRED`, `OTP_RATE_LIMITED`, `JOB_TAKEN`,
`JOB_NOT_ACTIVE`, `ASSIGNMENT_EXPIRED`, `LOAD_FULL`, `NOTHING_TO_DELIVER`.

## Auth

Collectors are registered by the operations team. There is no sign-up: the collector code and mobile
number must already match an account.

### `POST /auth/otp/request`

```json
// request
{ "collectorCode": "COL-0457", "mobile": "+94771234567" }
// 200
{ "challengeId": "otp_8f2c", "expiresAt": "2026-11-07T03:23:00Z", "resendAfterSeconds": 24 }
```

`mobile` is E.164. Errors: 404 `COLLECTOR_NOT_FOUND` when the code and number don't belong to the same
collector, 429 `OTP_RATE_LIMITED`.

### `POST /auth/otp/verify`

```json
// request
{ "challengeId": "otp_8f2c", "code": "482913" }
// 200
{
  "accessToken": "eyJ...",
  "refreshToken": "rt_...",
  "expiresAt": "2026-11-07T04:18:00Z",
  "collector": { "...": "see GET /me" }
}
```

Errors: 400 `INVALID_OTP`, 400 `OTP_EXPIRED`.

### `POST /auth/refresh`

```json
// request
{ "refreshToken": "rt_..." }
// 200: same token fields as /auth/otp/verify, without collector
{ "accessToken": "eyJ...", "refreshToken": "rt_...", "expiresAt": "2026-11-07T05:18:00Z" }
```

### `POST /auth/logout`

Empty body, 204. Revokes the refresh token and sets the collector offline.

## Collector

### `GET /me`

```json
{
  "id": "col_457",
  "collectorCode": "COL-0457",
  "firstName": "Ruwan",
  "lastName": "Silva",
  "mobile": "+94771234567",
  "memberSince": "2025-03-01",
  "language": "en",
  "online": true,
  "radiusKm": 5,
  "vehicle": { "type": "threeWheeler", "plate": "WP ABC-1234", "capacityKg": 120 },
  "area": { "name": "Colombo South", "suburbs": ["Dehiwala", "Wellawatte", "Nugegoda"] },
  "scale": { "name": "Scale BT-200", "paired": true, "autoConnect": true },
  "customerRatePerKgCents": 12000,
  "collectorRatePerKgCents": 4000
}
```

`vehicle.type` is `threeWheeler`, `motorbike`, `van` or `lorry`. `scale` is `null` when no scale is paired.
`customerRatePerKgCents` is what the customer is paid; `collectorRatePerKgCents` is what the collector
earns. Both are set by the server and may change, so the app never hard-codes them.

### `PATCH /me`

```json
// request: any subset
{ "language": "si", "scaleAutoConnect": false }
```

Returns the updated collector.

### `PUT /me/status`

```json
// request: latitude and longitude are null when the position is unknown
{ "online": true, "latitude": 6.8511, "longitude": 79.8655 }
// 200
{ "online": true, "radiusKm": 5 }
```

Going online starts job offers within `radiusKm`. While online, the app sends this again with a fresh
position whenever the collector has moved more than 200 m.

### `GET /me/dashboard`

Everything the Home screen needs in one call.

```json
{
  "today": { "jobsDone": 5, "collectedKg": 33.0, "earnedCents": 132000 },
  "load": { "weightKg": 20.5, "capacityKg": 120, "jobCount": 3 },
  "activeJob": {
    "id": "RT-24817",
    "customerFirstName": "Nadeesha",
    "area": "Dehiwala",
    "distanceKm": 1.2,
    "etaMinutes": 6,
    "windowStart": "2026-11-07T04:30:00Z",
    "windowEnd": "2026-11-07T05:30:00Z",
    "slot": "morning",
    "type": "bulk",
    "estimatedKg": 8.0,
    "itemCount": null,
    "bagCount": 3,
    "source": "callCentre",
    "latitude": 6.8511,
    "longitude": 79.8655
  },
  "nearby": { "jobCount": 3, "closestKm": 2.4 },
  "unreadNotifications": 4
}
```

`activeJob` is the job the collector has accepted or started, in the shape of a `GET /jobs` item, or `null`.
`nearby` counts open jobs within the collector's radius, and is `null` while offline.

## Jobs

### `GET /jobs?radiusKm=5&slot=morning&size=small`

Open jobs near the collector, closest first. All filters are optional. `slot` is `morning` or
`afternoon`; `size` is `small` (under 5 kg), `medium` (5–10 kg) or `large` (10 kg and over), by the
customer's estimate.

```json
{
  "items": [
    {
      "id": "RT-24822",
      "customerFirstName": "Mohamed",
      "area": "Wellawatte",
      "distanceKm": 2.4,
      "etaMinutes": 11,
      "windowStart": "2026-11-07T05:30:00Z",
      "windowEnd": "2026-11-07T06:30:00Z",
      "slot": "morning",
      "type": "itemised",
      "estimatedKg": 5.5,
      "itemCount": 14,
      "bagCount": 2,
      "source": "app",
      "latitude": 6.8742,
      "longitude": 79.8606
    }
  ]
}
```

`type` is `bulk` or `itemised`; `itemCount` is `null` for bulk. `source` is `app` or `callCentre`, and the
app tags call-centre jobs.

### `GET /jobs/{id}`

```json
{
  "id": "RT-24817",
  "status": "accepted",
  "customerFirstName": "Nadeesha",
  "customerName": "Nadeesha Perera",
  "customerPhone": "+94774821190",
  "area": "Dehiwala",
  "address": "42/3 Galle Road, Dehiwala",
  "distanceKm": 1.2,
  "etaMinutes": 6,
  "windowStart": "2026-11-07T04:30:00Z",
  "windowEnd": "2026-11-07T05:30:00Z",
  "slot": "morning",
  "type": "bulk",
  "estimatedKg": 8.0,
  "itemCount": null,
  "bagCount": 3,
  "source": "callCentre",
  "latitude": 6.8511,
  "longitude": 79.8655,
  "note": "Bags are next to the side gate. Please ring twice — gate code 1144.",
  "items": [
    { "id": "itm_1", "name": "T-shirts & shirts", "size": null, "quantity": null, "bagLabel": "Bag 1", "condition": "good" },
    { "id": "itm_2", "name": "Trousers & denim", "size": null, "quantity": null, "bagLabel": "Bag 2", "condition": "fair" },
    { "id": "itm_3", "name": "Bedsheets & towels", "size": null, "quantity": null, "bagLabel": "Bag 3", "condition": "worn" }
  ]
}
```

`status` is one of:

| Status | Meaning |
| --- | --- |
| `open` | On the job board |
| `accepted` | Held for this collector. Nobody else sees it. |
| `onTheWay` | Trip started |
| `arrived` | At the customer, checking and weighing |
| `collected` | Weighed and paid. Now in the collector's load. |
| `deliveredToHub` | Dropped at a hub |
| `cancelled` | Cancelled by the customer or the call centre |

`customerName` and `customerPhone` are `null` until this collector accepts the job; before that the app
shows `customerFirstName`. `address` is always present, so a collector can judge the trip before accepting. An item has a `quantity` and usually a `size` on itemised jobs, and a
`bagLabel` on bulk ones. `condition` is the customer's own description: `good`, `fair` or `worn`.

### `GET /jobs/active`

The job the collector is working on, in the same shape as `GET /jobs/{id}`, or 204 when there is none.

### `POST /jobs/{id}/accept`

Empty body. Returns the job with `status: accepted`. Errors: 409 `JOB_TAKEN` when another collector got
there first.

### `POST /jobs/{id}/start`

Empty body. Returns the job with `status: onTheWay`. The customer is told the collector is coming.

### `POST /jobs/{id}/arrive`

Empty body. Returns the job with `status: arrived`.

### `POST /jobs/{id}/photos`

`multipart/form-data` with `kind` (`customer` or `bag`) and `file` (JPEG, at most 2 MB after the app
resizes it).

```json
{ "photoId": "pho_31" }
```

### `POST /jobs/{id}/collection`

Confirms the collection and pays the customer. It can't be undone.

```json
// request
{
  "weightKg": 7.4,
  "weightSource": "scale",
  "bagCount": 3,
  "checkedItemIds": ["itm_1", "itm_2", "itm_3"],
  "photoIds": ["pho_31"]
}
// 200
{
  "jobId": "RT-24817",
  "customerFirstName": "Nadeesha",
  "weightKg": 7.4,
  "ratePerKgCents": 12000,
  "payoutCents": 88800,
  "load": { "weightKg": 27.9, "capacityKg": 120, "jobCount": 4 }
}
```

`weightSource` is `scale` or `manual`. The server works out `payoutCents` from the weight and its own rate,
so the amount the app showed before confirming is only a preview. Errors: 409 `JOB_NOT_ACTIVE`,
409 `LOAD_FULL`.

## Call-centre assignments

The call centre can offer a job straight to one collector. The collector has a short time to answer
before it goes to someone else.

### `GET /assignments/current`

```json
{
  "id": "asg_12",
  "expiresAt": "2026-11-07T04:08:45Z",
  "job": { "...": "see GET /jobs/{id}" }
}
```

204 when there is no pending offer. Until push notifications arrive, the app polls this every 15 seconds
while the collector is online.

### `POST /assignments/{id}/accept`

Empty body. Returns the job with `status: accepted`. Errors: 409 `ASSIGNMENT_EXPIRED`.

### `POST /assignments/{id}/decline`

```json
// request
{ "reason": "vehicleFull" }
```

204. `reason` is `vehicleFull`, `tooFar`, `endingShift`, `vehicleProblem` or `other`.

## Load and hubs

### `GET /load`

What the collector is carrying: collected jobs not yet delivered to a hub.

```json
{
  "weightKg": 20.5,
  "capacityKg": 120,
  "vehiclePlate": "WP ABC-1234",
  "items": [
    { "jobId": "RT-24790", "area": "Wellawatte", "collectedAt": "2026-11-07T03:10:00Z", "weightKg": 6.2 }
  ]
}
```

### `GET /hubs`

```json
{
  "items": [
    { "id": "hub_plg", "code": "PLG", "name": "Central Hub — Peliyagoda", "closesAt": "18:00" }
  ]
}
```

`closesAt` is local time, `HH:mm`.

### `POST /hub-deliveries`

```json
// request
{ "hubId": "hub_plg", "jobIds": ["RT-24790", "RT-24796", "RT-24803"] }
// 200
{
  "receiptId": "HUB-PLG-110426-0457",
  "hubName": "Central Hub — Peliyagoda",
  "jobCount": 3,
  "weightKg": 20.5,
  "customersNotified": 3
}
```

Each customer is told their clothes reached the hub. Errors: 409 `NOTHING_TO_DELIVER` when a job is no
longer in the load.

## Earnings

### `GET /earnings?period=today`

`period` is `today` or `week`.

```json
{
  "period": "today",
  "totalCents": 132000,
  "ratePerKgCents": 4000,
  "jobCount": 5,
  "collectedKg": 33.0,
  "jobs": [
    { "jobId": "RT-24803", "area": "Kirulapone", "completedAt": "2026-11-07T04:20:00Z", "weightKg": 4.5, "status": "collected" }
  ]
}
```

`status` is `collected` (still in the load) or `atHub`. Collectors are paid weekly to their bank account;
that is handled outside the app.

## Notifications

### `GET /notifications?cursor=`

```json
{
  "items": [
    {
      "id": "ntf_9",
      "type": "assignment",
      "title": "Call centre assignment",
      "body": "Nugegoda · 2.9 km · Bulk ~10 kg. Tap to answer.",
      "createdAt": "2026-11-07T04:08:00Z",
      "readAt": null,
      "targetType": "assignment",
      "targetId": "asg_12"
    }
  ],
  "unreadCount": 4,
  "nextCursor": null
}
```

`type` is `assignment`, `newJob`, `jobCancelled`, `hubReminder` or `payout`. `targetType` is `assignment`,
`job`, `load`, `earnings` or `null`, and decides where a tap goes.

### `POST /notifications/{id}/read`

204. Sent when the collector opens a notification.

### `POST /notifications/read-all`

204.

## Not in this contract yet

- Push notifications. Needs Firebase and a `POST /devices` endpoint to register tokens. Until then the app
  polls for call-centre assignments.
- Bluetooth scale readings stay on the device; only the final weight is sent.
- Offline queueing. The design shows "saved offline, will sync"; the replay rules (idempotency keys on
  `POST /jobs/{id}/collection`) need agreeing with the backend first.
- Route directions. The app hands off to Google Maps for turn-by-turn navigation.
