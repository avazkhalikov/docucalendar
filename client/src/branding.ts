/**
 * Whose calendar site this is, seen from the host it is served on.
 *
 * The same build serves docurest.com, calendar.docurest.com and every white-label portal. On a
 * portal the page must not say "DocuCalendar" — it is the university's own site as far as its
 * staff are concerned. Two sources, in order:
 *
 *  1. `window.__PORTAL_BRAND__`, substituted into the HTML by nginx on portal hosts. Instant, no
 *     request, and the value is there before the first paint.
 *  2. `GET /api/portal/branding?host=…` on this very origin — on a portal that path reaches
 *     Docurest, which knows every white label. On calendar.docurest.com it reaches the calendar
 *     API instead and 404s, which is the right answer there anyway.
 *
 * Anything else: "DocuCalendar". Nothing here blocks rendering; the brand arrives and the header
 * updates.
 */

export interface Branding {
  brand: string;
  initials: string;
  accentColor?: string;
  logoUrl?: string;
}

export const DEFAULT_BRAND = 'DocuCalendar';

/** nginx leaves the literal token in place when it has nothing to substitute. */
function injected(): string | null {
  if (typeof window === 'undefined') return null;
  const value = (window as unknown as { __PORTAL_BRAND__?: string }).__PORTAL_BRAND__;
  return typeof value === 'string' && value.length > 0 && value !== '__PORTAL_BRAND__' ? value : null;
}

export function initialsOf(brand: string): string {
  const words = brand.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return 'DC';
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

let cached: Branding | null = null;

export function brandingNow(): Branding {
  if (cached) return cached;
  const name = injected();
  return name ? { brand: name, initials: initialsOf(name) } : { brand: DEFAULT_BRAND, initials: 'DC' };
}

/** Resolves the brand, asking the host's API only when nothing was injected. */
export async function resolveBranding(): Promise<Branding> {
  if (cached) return cached;

  const name = injected();
  if (name) {
    cached = { brand: name, initials: initialsOf(name) };
    return cached;
  }

  try {
    const response = await fetch(`/api/portal/branding?host=${encodeURIComponent(window.location.host)}`, {
      headers: { Accept: 'application/json' },
    });
    if (response.ok) {
      const body = (await response.json()) as Partial<Branding>;
      if (body?.brand) {
        cached = {
          brand: body.brand,
          initials: body.initials || initialsOf(body.brand),
          accentColor: body.accentColor,
          logoUrl: body.logoUrl,
        };
        return cached;
      }
    }
  } catch {
    /* no branding service on this host — the default is correct */
  }

  cached = { brand: DEFAULT_BRAND, initials: 'DC' };
  return cached;
}

/** The tab title, brand-aware: "Schedule · WIUT AI Assistant". */
export function applyTitle(brand: string, page?: string): void {
  document.title = page ? `${page} · ${brand}` : brand;
}
