import { WebSearchTool } from "../types";

type SafeSearchSetting = "Off" | "Moderate" | "Strict";

type SearchOptions = { recencyDays?: number; max?: number };

type SearchResult = {
  title: string;
  url: string;
  snippet?: string;
  publishDateIso?: string | null;
};

export class BingWebSearch implements WebSearchTool {
  private readonly apiKey: string;
  private readonly market: string;
  private readonly safeSearch: SafeSearchSetting;

  constructor(opts: { apiKey: string; market?: string; safeSearch?: SafeSearchSetting }) {
    if (!opts || !opts.apiKey) {
      throw new Error("BingWebSearch requires an apiKey");
    }
    this.apiKey = opts.apiKey;
    this.market = opts.market ?? "en-GB";
    this.safeSearch = opts.safeSearch ?? "Moderate";
  }

  async searchWeb(query: string, opts?: SearchOptions): Promise<SearchResult[]> {
    const params = new URLSearchParams();
    params.set("q", query);
    params.set("mkt", this.market);
    const count = Math.max(1, Math.min(opts?.max ?? 10, 50));
    params.set("count", String(count));
    params.set("safeSearch", this.safeSearch);
    const freshness = toFreshness(opts?.recencyDays);
    if (freshness) {
      params.set("freshness", freshness);
    }
    const endpoint = `https://api.bing.microsoft.com/v7.0/search?${params.toString()}`;

    try {
      const response = await fetch(endpoint, {
        headers: { "Ocp-Apim-Subscription-Key": this.apiKey },
      });
      if (!response.ok) {
        console.warn(`BingWebSearch HTTP ${response.status} ${response.statusText}`);
        return [];
      }
      const data = await response.json();
      const items = Array.isArray(data?.webPages?.value) ? data.webPages.value : [];
      const results: SearchResult[] = [];
      for (const item of items) {
        if (!item || typeof item.url !== "string") {
          continue;
        }
        const title = typeof item.name === "string" && item.name.trim() ? item.name.trim() : item.url;
        const snippet = typeof item.snippet === "string" ? item.snippet : undefined;
        const publish = sanitizeDate(item.datePublished ?? item.dateLastCrawled);
        results.push({ title, url: item.url, snippet, publishDateIso: publish });
      }
      return results;
    } catch (error) {
      console.warn(`BingWebSearch error ${(error as Error).message ?? String(error)}`);
      return [];
    }
  }

  async openUrl(url: string): Promise<{ ok: boolean; finalUrl: string; text?: string }> {
    try {
      const response = await fetch(url, { redirect: "follow" });
      const finalUrl = response.url ?? url;
      if (!response.ok) {
        return { ok: false, finalUrl };
      }
      const text = await readLimitedText(response, 200_000);
      return { ok: true, finalUrl, text };
    } catch {
      return { ok: false, finalUrl: url };
    }
  }
}

function toFreshness(days?: number): string | undefined {
  if (days === undefined) {
    return undefined;
  }
  if (days <= 1) {
    return "Day";
  }
  if (days <= 7) {
    return "Week";
  }
  return "Month";
}

function sanitizeDate(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return null;
  }
  return date.toISOString();
}

async function readLimitedText(response: Response, maxBytes: number): Promise<string> {
  if (!response.body) {
    const buffer = await response.arrayBuffer();
    const slice = buffer.byteLength > maxBytes ? buffer.slice(0, maxBytes) : buffer;
    return new TextDecoder().decode(slice);
  }
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let received = 0;
  while (received < maxBytes) {
    const { done, value } = await reader.read();
    if (done || !value) {
      break;
    }
    let chunk = value;
    if (received + chunk.length > maxBytes) {
      const remaining = maxBytes - received;
      if (remaining <= 0) {
        break;
      }
      chunk = chunk.subarray(0, remaining);
    }
    chunks.push(chunk);
    received += chunk.length;
    if (received >= maxBytes) {
      break;
    }
  }
  reader.releaseLock();
  const totalLength = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
  const merged = new Uint8Array(totalLength);
  let offset = 0;
  for (const chunk of chunks) {
    merged.set(chunk, offset);
    offset += chunk.length;
  }
  return new TextDecoder().decode(merged);
}
