# Data Model — Phase 2: Catalog

> Trimmed shapes. Sources: `openapi.catalog.json`, `openapi.pricing.json`, `openapi.inventory.json`, `openapi.reviews.json`.

## Catalog

### GET `/v1/customer/catalog/categories`
Response:
```json
[
  { "id": "uuid", "slug": "string", "name": "string", "iconUrl": "string?", "parentId": "uuid?" }
]
```

### GET `/v1/customer/catalog/brands`
Response:
```json
[
  { "id": "uuid", "slug": "string", "name": "string", "logoUrl": "string?" }
]
```

### GET `/v1/customer/catalog/categories/{slug}/products`
Query: `market`, `page`, `pageSize`, `sort`, `brand`, `priceMin`, `priceMax`, `restricted`.
Response: see §S-2.3 in `spec.md`.

### GET `/v1/customer/catalog/products/{slug}`
Response: see §S-2.6 in `spec.md`.

## Pricing

### POST `/customer/pricing/price-cart` (preview mode)
Request:
```json
{
  "lines": [{ "productId": "uuid", "qty": 1 }],
  "couponCode": "string?",
  "marketCode": "SA | EG",
  "buyerKind": "consumer | business | guest"
}
```
Response:
```json
{
  "total": { "amount": "120.00", "currency": "SAR" },
  "lines": [
    {
      "productId": "uuid",
      "qty": 1,
      "unitPrice": "120.00",
      "discount": "0.00",
      "lineTotal": "120.00",
      "tierLabel": "consumer | business"
    }
  ],
  "appliedPromotions": [{ "code": "string", "amount": "0.00", "kind": "coupon | promotion | bundle" }],
  "explanationToken": "string"
}
```

## Inventory

### GET `/v1/customer/inventory/availability`
Query: `productIds` (comma-separated UUIDs), `market`.
Response:
```json
[
  {
    "productId": "uuid",
    "inStock": true,
    "lowStock": false,
    "earliestDeliveryDate": "iso8601-date",
    "warehouseHint": "string?"
  }
]
```

## Public reviews aggregates

### GET `/v1/public/reviews/aggregates?product_ids=…&market_code=…`
Response:
```json
[
  { "productId": "uuid", "ratingAverage": 4.7, "ratingCount": 125, "starHistogram": [3, 7, 12, 28, 75] }
]
```

### GET `/v1/public/reviews/aggregates/{product_id}?market_code=…`
Response: single element of the array above.

## Local cache schema (`CatalogGatewayImpl`)

| Cache key | Value | TTL |
|---|---|---|
| `cat:{locale}:{market}:categories` | category list | 5 min |
| `cat:{locale}:{market}:brands` | brand list | 5 min |
| `cat:{locale}:{market}:cat/{slug}/products:{queryHash}` | products page | 5 min |
| `cat:{locale}:{market}:product/{slug}` | product detail | 5 min |
| `prc:{locale}:{market}:product/{id}:qty/{n}` | last price preview | 60 s |
| `inv:{market}:product/{id}` | availability | 60 s |
| `rev:{market}:product/{id}` | aggregate | 5 min |

Cache cleared on any `SessionState` change that touches `locale` or `marketCode`.
