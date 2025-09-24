export type WikipediaSummary = {
  title: string;
  normalizedTitle: string;
  lang: string;
  url: string;
  extract?: string;
  description?: string;
  lastModified?: string;
};

type WikipediaProviderOptions = {
  userAgent?: string;
  apiKey?: string;
};

type FetchSummaryOptions = {
  address?: string;
};

export class WikipediaProvider {
  constructor(private readonly opts: WikipediaProviderOptions = {}) {}

  async fetchSummary(
    title: string,
    lang: string,
    options: FetchSummaryOptions = {}
  ): Promise<WikipediaSummary | null> {
    const safeLang = this.normalizeLang(lang);
    const candidates = this.buildTitleCandidates(title, options.address);
    if (!candidates.length) {
      return null;
    }

    const seen = new Set<string>();
    for (const candidate of candidates) {
      const normalized = candidate.replace(/\s+/g, "_");
      if (!normalized) {
        continue;
      }
      const key = `${safeLang}:${normalized.toLowerCase()}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      const encodedTitle = encodeURIComponent(normalized);

      const primaryUrl = `https://api.wikimedia.org/core/v1/wikipedia/${safeLang}/page/summary/${encodedTitle}`;
      const summaryFromPrimary = await this.tryFetch(primaryUrl, safeLang, candidate, {
        includeApiKey: true,
      });
      if (summaryFromPrimary) {
        return summaryFromPrimary;
      }

      const fallbackUrl = `https://${safeLang}.wikipedia.org/api/rest_v1/page/summary/${encodedTitle}`;
      const fallback = await this.tryFetch(fallbackUrl, safeLang, candidate, { includeApiKey: false });
      if (fallback) {
        return fallback;
      }
    }

    return null;
  }

  private normalizeLang(lang: string | undefined): string {
    const trimmed = typeof lang === "string" ? lang.trim().toLowerCase() : "";
    if (!trimmed) {
      return "en";
    }
    const safe = trimmed.replace(/[^a-z0-9_-]/gi, "");
    return safe || "en";
  }

  private buildTitleCandidates(title: string, address?: string): string[] {
    const candidates: string[] = [];
    const seen = new Set<string>();
    const addCandidate = (value: string | null | undefined) => {
      const cleaned = this.cleanCandidate(value);
      if (!cleaned) {
        return;
      }
      const key = cleaned.toLowerCase();
      if (seen.has(key)) {
        return;
      }
      seen.add(key);
      candidates.push(cleaned);
    };

    const baseTitle = this.cleanCandidate(title);
    addCandidate(baseTitle);

    const cleanedAddress = this.cleanCandidate(address);
    if (!cleanedAddress) {
      return candidates;
    }

    const parts = this.parseAddressParts(cleanedAddress);
    addCandidate(cleanedAddress);
    addCandidate(parts.street);
    addCandidate(parts.city);
    addCandidate(parts.country);

    if (parts.street && parts.city) {
      addCandidate(`${parts.street}, ${parts.city}`);
      addCandidate(`${parts.street} ${parts.city}`);
    }

    if (baseTitle && parts.city) {
      addCandidate(`${baseTitle}, ${parts.city}`);
      addCandidate(`${baseTitle} (${parts.city})`);
      addCandidate(`${baseTitle} ${parts.city}`);
    }

    if (baseTitle && parts.country) {
      addCandidate(`${baseTitle}, ${parts.country}`);
      addCandidate(`${baseTitle} (${parts.country})`);
    }

    return candidates;
  }

  private parseAddressParts(address: string): {
    street?: string;
    city?: string;
    country?: string;
  } {
    const segments = address
      .split(",")
      .map((segment) => this.cleanCandidate(segment))
      .filter((segment): segment is string => Boolean(segment));

    if (!segments.length) {
      return {};
    }

    const street = segments[0];
    let city: string | undefined;
    let country: string | undefined;

    if (segments.length >= 3) {
      country = segments[segments.length - 1];
      city = segments[segments.length - 2];
    } else if (segments.length === 2) {
      city = segments[1];
    }

    const normalizedCity = city ? this.stripPostalCode(city) : undefined;
    const normalizedCountry = country ? this.stripPostalCode(country) : undefined;

    return {
      street,
      city: normalizedCity || city,
      country: normalizedCountry || country,
    };
  }

  private cleanCandidate(value: string | null | undefined): string | null {
    if (typeof value !== "string") {
      return null;
    }
    const trimmed = value.replace(/\s+/g, " ").trim();
    return trimmed.length ? trimmed : null;
  }

  private stripPostalCode(value: string): string {
    return value.replace(/\b\d{3,5}\b/g, " ").replace(/\s{2,}/g, " ").trim();
  }

  private async tryFetch(
    url: string,
    lang: string,
    fallbackTitle: string,
    opts: { includeApiKey: boolean }
  ): Promise<WikipediaSummary | null> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10_000);
    try {
      const headers: Record<string, string> = {
        Accept: "application/json; charset=utf-8",
        "Accept-Language": lang,
        "User-Agent": this.opts.userAgent ?? "building-facts-kit/1.0",
      };
      if (opts.includeApiKey && this.opts.apiKey) {
        headers.Authorization = `Bearer ${this.opts.apiKey}`;
      }
      const response = await fetch(url, {
        method: "GET",
        headers,
        signal: controller.signal,
      });
      if (response.status === 404) {
        return null;
      }
      if (!response.ok) {
        return null;
      }
      const text = await response.text();
      if (!text || text.length > 1_000_000) {
        return null;
      }
      const data = JSON.parse(text);
      return this.parseSummary(data, lang, fallbackTitle);
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }

  private parseSummary(raw: any, lang: string, fallbackTitle: string): WikipediaSummary | null {
    if (!raw || typeof raw !== "object") {
      return null;
    }

    const langValue = this.extractString(raw.lang) || lang;
    const titles = raw.titles && typeof raw.titles === "object" ? raw.titles : {};
    const normalizedTitle =
      this.extractString(titles.normalized) ||
      this.extractString(titles.canonical) ||
      this.extractString(raw.title) ||
      fallbackTitle;
    const displayTitle = this.extractString(raw.displaytitle) || this.extractString(raw.title) || normalizedTitle;

    const contentUrls = raw.content_urls && typeof raw.content_urls === "object" ? raw.content_urls : {};
    const desktop = contentUrls.desktop && typeof contentUrls.desktop === "object" ? contentUrls.desktop : {};
    const mobile = contentUrls.mobile && typeof contentUrls.mobile === "object" ? contentUrls.mobile : {};
    const desktopUrl = this.extractString(desktop.page);
    const mobileUrl = this.extractString(mobile.page);
    const fallbackUrl = `https://${langValue}.wikipedia.org/wiki/${encodeURIComponent(normalizedTitle.replace(/\s+/g, "_"))}`;
    const pageUrl = desktopUrl || mobileUrl || fallbackUrl;

    const extract = this.extractString(raw.extract);
    const description = this.extractString(raw.description);
    const timestamp = this.extractString(raw.timestamp);

    return {
      title: displayTitle || normalizedTitle,
      normalizedTitle,
      lang: langValue,
      url: pageUrl,
      extract: extract || undefined,
      description: description || undefined,
      lastModified: timestamp || undefined,
    };
  }

  private extractString(value: unknown): string | null {
    if (typeof value !== "string") {
      return null;
    }
    const trimmed = value.trim();
    return trimmed.length ? trimmed : null;
  }
}
