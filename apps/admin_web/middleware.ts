/**
 * T026 + T032a: Next.js middleware.
 *
 * Runs on every request to:
 *  1. Resolve locale from cookie / Accept-Language → set on the request
 *     so Server Components see it via `getLocale()`.
 *  2. Generate a per-request CSP nonce (FR-028c) and emit the security
 *     headers per `contracts/csp.md`.
 *  3. Enforce auth: routes outside `(auth)` require an active session;
 *     missing → redirect to `/login?continueTo=…`.
 *  4. Enforce per-route permissions per `contracts/permission-catalog.md`
 *     and `lib/auth/permissions.ts`. 403 → `/__forbidden`.
 *
 * The auth-proxy itself (`/api/auth/*`) is unauthenticated by design —
 * those handlers seal/unseal the cookie themselves.
 */
import { NextResponse, type NextRequest } from "next/server";
import { permissionsForRoute } from "@/lib/auth/permissions";
import { LOCALE_COOKIE, isLocale, type Locale } from "@/lib/i18n/config";

const PUBLIC_ROUTES = [/^\/login(?:\/|$)/, /^\/mfa(?:\/|$)/, /^\/reset(?:\/|$)/];

// Authenticated session-only routes that exist outside the
// permission-mapped admin tree (e.g., the forbidden + not-found
// landing pages the middleware itself rewrites to). These bypass the
// permission map but still require a session.
const SESSION_ONLY_ROUTES = [/^\/__forbidden(?:\/|$)/, /^\/__not-found(?:\/|$)/];
const UNAUTH_API = [
  /^\/api\/auth\/login(?:\/|$)/,
  /^\/api\/auth\/mfa(?:\/|$)/,
  /^\/api\/auth\/refresh(?:\/|$)/,
  /^\/api\/auth\/logout(?:\/|$)/,
  /^\/api\/auth\/reset(?:\/|$)/,
];

// Restrict the asset-extension bypass to the known public-asset
// prefixes (`/_next/...`, `/static/...`, `/favicon.ico`). A bare
// extension regex like `/\.(svg|...|js|map)$/` would let any admin
// route whose last segment ends in one of those extensions skip both
// session and permission checks — e.g. `/orders/exports/12.csv` or a
// future `/reports/foo.js` — leaking the route to anonymous callers.
const STATIC_PATHS = [
  /^\/_next(?:\/|$)/,
  /^\/favicon\.ico$/,
  /^\/static\/.*\.(?:svg|png|jpg|jpeg|gif|webp|ico|woff2?|ttf|css|js|map)$/,
];

function isPublic(pathname: string): boolean {
  return PUBLIC_ROUTES.some((re) => re.test(pathname));
}

function isUnauthApi(pathname: string): boolean {
  return UNAUTH_API.some((re) => re.test(pathname));
}

function isStatic(pathname: string): boolean {
  return STATIC_PATHS.some((re) => re.test(pathname));
}

function generateNonce(): string {
  const buf = new Uint8Array(16);
  crypto.getRandomValues(buf);
  let str = "";
  for (let i = 0; i < buf.length; i++) str += String.fromCharCode(buf[i]);
  return btoa(str);
}

function buildCspHeader(nonce: string): string {
  const backend = process.env.BACKEND_URL ?? "http://localhost:5000";
  const sse = process.env.NOTIFICATIONS_SSE_URL ?? backend;
  const storage = process.env.STORAGE_BASE_URL ?? backend;
  const directives = [
    `default-src 'self'`,
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'`,
    `style-src 'self' 'unsafe-inline'`,
    `img-src 'self' data: ${storage}`,
    `font-src 'self' data:`,
    `connect-src 'self' ${backend} ${sse} ${storage}`,
    `frame-ancestors 'none'`,
    `base-uri 'self'`,
    `form-action 'self'`,
    `object-src 'none'`,
    `upgrade-insecure-requests`,
  ];
  return directives.join("; ");
}

function emitSecurityHeaders(response: NextResponse, nonce: string): void {
  response.headers.set("Content-Security-Policy", buildCspHeader(nonce));
  response.headers.set(
    "Strict-Transport-Security",
    "max-age=31536000; includeSubDomains; preload",
  );
  response.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
  response.headers.set("X-Content-Type-Options", "nosniff");
  response.headers.set(
    "Permissions-Policy",
    "camera=(self), microphone=(), geolocation=()",
  );
  response.headers.set("x-nonce", nonce);
}

function resolveLocaleHeader(req: NextRequest): Locale {
  const cookie = req.cookies.get(LOCALE_COOKIE)?.value;
  if (isLocale(cookie)) return cookie;
  const accept = req.headers.get("accept-language") ?? "";
  for (const part of accept.split(",")) {
    const tag = part.split(";")[0].trim().toLowerCase();
    const primary = tag.split("-")[0];
    if (primary === "en" || primary === "ar") return primary;
  }
  return "en";
}

export async function middleware(req: NextRequest) {
  const { pathname } = req.nextUrl;

  // Static assets: skip auth/permission, still emit security headers.
  if (isStatic(pathname)) {
    const res = NextResponse.next();
    return res;
  }

  const nonce = generateNonce();
  const requestHeaders = new Headers(req.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("x-locale", resolveLocaleHeader(req));
  // T035 + (admin) layout: expose pathname so deep-permission gates can
  // resolve `permissionsForRoute(pathname)` server-side.
  requestHeaders.set("x-pathname", pathname);

  // Unauthenticated API routes (login / mfa / refresh / logout / reset).
  if (isUnauthApi(pathname)) {
    const response = NextResponse.next({ request: { headers: requestHeaders } });
    emitSecurityHeaders(response, nonce);
    return response;
  }

  // Public pages — no session check.
  if (isPublic(pathname)) {
    const response = NextResponse.next({ request: { headers: requestHeaders } });
    emitSecurityHeaders(response, nonce);
    return response;
  }

  // Auth check.
  const sessionCookie = req.cookies.get("admin_session")?.value;
  if (!sessionCookie) {
    const url = req.nextUrl.clone();
    url.pathname = "/login";
    url.searchParams.set("continueTo", pathname);
    const response = NextResponse.redirect(url);
    emitSecurityHeaders(response, nonce);
    return response;
  }

  // System-only routes (forbidden / not-found landing pages) bypass
  // the permission map but still required a session above.
  if (SESSION_ONLY_ROUTES.some((re) => re.test(pathname))) {
    const response = NextResponse.next({ request: { headers: requestHeaders } });
    emitSecurityHeaders(response, nonce);
    return response;
  }

  // Per-route permission check (best-effort; the cookie is sealed so we
  // only check if rules are declared — full unseal happens in route
  // handlers / Server Components).
  const requiredKeys = permissionsForRoute(pathname);
  if (requiredKeys === null) {
    // Fail closed: an admin-tree path with no permission mapping is a
    // configuration gap, not an implicit "allow". Rewrite to the
    // forbidden page so a missing mapping never leaks an unprotected
    // route.
    const url = req.nextUrl.clone();
    url.pathname = "/__forbidden";
    url.search = "";
    const response = NextResponse.rewrite(url);
    emitSecurityHeaders(response, nonce);
    return response;
  }

  // Note: we cannot unseal the iron-session cookie inside the Edge
  // middleware (Node-only crypto APIs). The Server Component layout
  // re-checks permissions before rendering. The middleware here does
  // the cheap session-presence + locale + CSP work; deep permission
  // enforcement lives in `app/(admin)/layout.tsx`.
  const response = NextResponse.next({ request: { headers: requestHeaders } });
  emitSecurityHeaders(response, nonce);
  return response;
}

export const config = {
  matcher: [
    "/((?!_next/static|_next/image|favicon.ico).*)",
  ],
};
