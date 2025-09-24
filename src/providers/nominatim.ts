export class NominatimGeocoder {
  constructor(private opts: { userAgent?: string } = {}) {}

  async geocodeAddress(address: string): Promise<{ lat: number; lon: number; displayName?: string } | null> {
    if (!address.trim()) {
      return null;
    }

    const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&q=${encodeURIComponent(address)}`;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10000);
    try {
      const response = await fetch(url, {
        method: "GET",
        headers: {
          "User-Agent": this.opts.userAgent ?? "building-facts-kit/1.0",
        },
        signal: controller.signal,
      });
      if (!response.ok) {
        return null;
      }
      const text = await response.text();
      if (text.length > 500_000) {
        return null;
      }
      const data = JSON.parse(text);
      if (!Array.isArray(data) || data.length === 0) {
        return null;
      }
      const first = data[0];
      const lat = Number.parseFloat(first.lat);
      const lon = Number.parseFloat(first.lon);
      if (!Number.isFinite(lat) || !Number.isFinite(lon)) {
        return null;
      }
      const displayName = typeof first.display_name === "string" ? first.display_name : undefined;
      return { lat, lon, displayName };
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }
}
