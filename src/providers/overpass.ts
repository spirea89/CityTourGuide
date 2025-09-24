export type OverpassFeature = {
  id: string;
  type: "node" | "way" | "relation";
  lat?: number;
  lon?: number;
  tags?: Record<string, string>;
};

export class OverpassProvider {
  constructor(
    private endpoint = "https://overpass-api.de/api/interpreter",
    private opts: { userAgent?: string } = {}
  ) {}

  async findNearestBuilding(lat: number, lon: number): Promise<OverpassFeature | null> {
    if (!Number.isFinite(lat) || !Number.isFinite(lon)) {
      return null;
    }

    const latStr = lat.toFixed(6);
    const lonStr = lon.toFixed(6);
    const query = `
[out:json][timeout:25];
(
  node(around:60, ${latStr}, ${lonStr})["building"];
  way(around:60, ${latStr}, ${lonStr})["building"];
  relation(around:60, ${latStr}, ${lonStr})["building"];
);
out center 1;
`;

    const body = `data=${encodeURIComponent(query)}`;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 12000);
    try {
      const response = await fetch(this.endpoint, {
        method: "POST",
        headers: {
          "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
          "User-Agent": this.opts.userAgent ?? "building-facts-kit/1.0",
        },
        body,
        signal: controller.signal,
      });
      if (!response.ok) {
        return null;
      }
      const text = await response.text();
      if (text.length > 2_000_000) {
        return null;
      }
      const json = JSON.parse(text);
      const elements: any[] = Array.isArray(json?.elements) ? json.elements : [];
      let best: any = null;
      let bestTags = -1;
      for (const element of elements) {
        if (!element || (element.type !== "node" && element.type !== "way" && element.type !== "relation")) {
          continue;
        }
        const tags: Record<string, string> = {};
        if (element.tags && typeof element.tags === "object") {
          for (const [key, value] of Object.entries(element.tags)) {
            if (typeof value === "string") {
              tags[key] = value;
            } else if (typeof value === "number" || typeof value === "boolean") {
              tags[key] = String(value);
            }
          }
        }
        const tagCount = Object.keys(tags).length;
        if (tagCount > bestTags) {
          best = { ...element, tags };
          bestTags = tagCount;
        }
      }
      if (!best) {
        return null;
      }
      const center = best.center ?? {};
      const feature: OverpassFeature = {
        id: String(best.id),
        type: best.type,
        lat:
          typeof best.lat === "number"
            ? best.lat
            : typeof center.lat === "number"
            ? center.lat
            : undefined,
        lon:
          typeof best.lon === "number"
            ? best.lon
            : typeof center.lon === "number"
            ? center.lon
            : undefined,
        tags: best.tags,
      };
      return feature;
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }
}
