# Data Model — Phase 3: Search

> Source: `openapi.search.json`.

## POST `/v1/customer/search/autocomplete`
Request:
```json
{ "query": "string", "marketCode": "SA | EG", "locale": "ar | en", "topMatchesLimit": 5 }
```
Response:
```json
{
  "suggestions": [
    { "label": "string", "kind": "term | category | brand", "linkSlug": "string?" }
  ],
  "topMatches": [
    {
      "productId": "uuid",
      "slug": "string",
      "name": "string",
      "imageUrl": "string",
      "priceHint": { "amount": "string", "currency": "string" }
    }
  ]
}
```

## POST `/v1/customer/search/products`
Request:
```json
{
  "query": "string",
  "marketCode": "SA | EG",
  "locale": "ar | en",
  "page": 1,
  "pageSize": 24,
  "sort": "relevance | priceAsc | priceDesc | new | rating",
  "facets": {
    "brand": ["brand-x"],
    "priceMin": "0.00",
    "priceMax": "999.00",
    "restricted": false
  }
}
```
Response:
```json
{
  "items": [ /* ProductCard shape from Phase 2 product list */ ],
  "page": 1,
  "pageSize": 24,
  "totalCount": 312,
  "facets": [
    { "key": "brand", "label": "string", "type": "checkbox", "options": [{ "value": "brand-x", "label": "Brand X", "count": 12 }] }
  ],
  "sortOptions": [{ "key": "relevance", "label": "Relevance" }]
}
```

## POST `/v1/customer/search/lookup`
Request:
```json
{ "sku": "string?", "barcode": "string?", "marketCode": "SA | EG" }
```
Response:
```json
{
  "matched": true,
  "match": { "productId": "uuid?", "slug": "string?", "name": "string?", "kind": "sku | barcode" }
}
```

## Local persistence

| Key | Value | Notes |
|---|---|---|
| `search.recent.{accountId or 'anon'}` | JSON array of strings, capped 10, LRU | cleared on user demand |
