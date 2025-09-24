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

export class WikipediaProvider {
  constructor(private readonly opts: WikipediaProviderOptions = {}) {}

  async fetchSummary(title: string, lang: string): Promise<WikipediaSummary | null> {
    const cleanedTitle = typeof title === "string" ? title.trim() : "";
    if (!cleanedTitle) {
      return null;
    }
    const safeLang = this.normalizeLang(lang);
    const targetTitle = cleanedTitle.replace(/\s+/g, "_");
    const encodedTitle = encodeURIComponent(targetTitle);

    const primaryUrl = `https://api.wikimedia.org/core/v1/wikipedia/${safeLang}/page/summary/${encodedTitle}`;
    const summaryFromPrimary = await this.tryFetch(primaryUrl, safeLang, cleanedTitle, {
      includeApiKey: true,
    });
    if (summaryFromPrimary) {
      return summaryFromPrimary;
    }

    const fallbackUrl = `https://${safeLang}.wikipedia.org/api/rest_v1/page/summary/${encodedTitle}`;
    return this.tryFetch(fallbackUrl, safeLang, cleanedTitle, { includeApiKey: false });
  }

  private normalizeLang(lang: string | undefined): string {
    const trimmed = typeof lang === "string" ? lang.trim().toLowerCase() : "";
    if (!trimmed) {
      return "en";
    }
    const safe = trimmed.replace(/[^a-z0-9_-]/gi, "");
    return safe || "en";
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
