import type { BuildingFact, BuildingFactsResult, Evidence, SourceQuality } from "./types.js";
import { NominatimGeocoder } from "../providers/nominatim.js";
import { OverpassProvider } from "../providers/overpass.js";
import type { OverpassFeature } from "../providers/overpass.js";
import { WikidataProvider } from "../providers/wikidata.js";
import type { WikidataFacts } from "../providers/wikidata.js";
import { WikipediaProvider } from "../providers/wikipedia.js";
import type { WikipediaSummary } from "../providers/wikipedia.js";

export type GetBuildingFactsInput = {
  address?: string;
  lat?: number;
  lon?: number;
  locale?: string;
  minSourcesPerFact?: number;
  nowIso?: string;
  overpassEndpoint?: string;
  userAgent?: string;
  wikipediaApiKey?: string;
};

type ProviderOverrides = Partial<{ geocoder: any; overpass: any; wikidata: any; wikipedia: any }>;

const providerOverrides: ProviderOverrides = {};

export const __testOnly = {
  setProviders(p: ProviderOverrides): void {
    if (Object.prototype.hasOwnProperty.call(p, "geocoder")) {
      const value = (p as any).geocoder;
      if (value == null) {
        delete providerOverrides.geocoder;
      } else {
        providerOverrides.geocoder = value;
      }
    }
    if (Object.prototype.hasOwnProperty.call(p, "overpass")) {
      const value = (p as any).overpass;
      if (value == null) {
        delete providerOverrides.overpass;
      } else {
        providerOverrides.overpass = value;
      }
    }
    if (Object.prototype.hasOwnProperty.call(p, "wikidata")) {
      const value = (p as any).wikidata;
      if (value == null) {
        delete providerOverrides.wikidata;
      } else {
        providerOverrides.wikidata = value;
      }
    }
    if (Object.prototype.hasOwnProperty.call(p, "wikipedia")) {
      const value = (p as any).wikipedia;
      if (value == null) {
        delete providerOverrides.wikipedia;
      } else {
        providerOverrides.wikipedia = value;
      }
    }
  },
};

const QUALITY_PRIORITY: Record<SourceQuality, number> = { high: 3, medium: 2, low: 1 };

function qualityRank(quality: SourceQuality): number {
  return QUALITY_PRIORITY[quality] ?? 0;
}

export function viennaNowIso(): string {
  const formatter = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Vienna",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
  return formatter.format(new Date());
}

export function trimWords(value: string, maxWords: number): string {
  const words = value.trim().split(/\s+/).filter(Boolean);
  if (words.length <= maxWords) {
    return words.join(" ");
  }
  return `${words.slice(0, maxWords).join(" ")}…`;
}

export function qualityFor(url: string): { q: SourceQuality; why: string } {
  try {
    const { hostname } = new URL(url);
    const host = hostname.toLowerCase();
    if (host.endsWith(".gv.at") || host.includes(".gov") || host.includes(".gob") || host.includes(".go.")) {
      return { q: "high", why: "Official government or municipal domain" };
    }
    if (host.includes("wikidata.org")) {
      return { q: "high", why: "Structured open data maintained by Wikidata" };
    }
    if (host.includes("unesco.org") || host.includes("europa.eu")) {
      return { q: "high", why: "International body or institutional publication" };
    }
    if (host.includes("wikipedia.org") || host.includes("wikimedia.org")) {
      return { q: "medium", why: "Community-maintained encyclopedia" };
    }
    if (host.includes("openstreetmap.org") || host.includes("osm.org") || host.includes("overpass-api.de")) {
      return { q: "medium", why: "OpenStreetMap community dataset" };
    }
    if (host.includes("blogspot") || host.includes("wordpress") || host.includes("medium.com")) {
      return { q: "low", why: "Self-published web content" };
    }
    if (host.includes(".edu") || host.includes(".ac.")) {
      return { q: "medium", why: "Academic or educational domain" };
    }
    return { q: "medium", why: "General independent source" };
  } catch {
    return { q: "low", why: "Unparseable source URL" };
  }
}

export function mergeEvidence(evidence: Evidence[]): Evidence[] {
  const map = new Map<string, Evidence>();
  for (const item of evidence) {
    const snippet = item.snippet ? trimWords(item.snippet, 25) : undefined;
    const normalized: Evidence = { ...item, snippet };
    let key = normalized.url;
    try {
      const parsed = new URL(normalized.url);
      key = `${parsed.host}${parsed.pathname}`;
    } catch {
      // keep original key
    }
    const existing = map.get(key);
    if (!existing || qualityRank(normalized.source_quality) > qualityRank(existing.source_quality)) {
      map.set(key, normalized);
    }
  }
  return Array.from(map.values());
}

export function pickVerdict(evidence: Evidence[], min: number): {
  verdict: "true" | "mixed" | "uncertain";
  confidence: number;
  note?: string;
} {
  if (!evidence.length) {
    return { verdict: "uncertain", confidence: 0 };
  }
  const uniqueSources = evidence.length;
  const highCount = evidence.filter((e) => e.source_quality === "high").length;
  if (uniqueSources >= min) {
    const base = 0.55;
    const extra = Math.min(0.25, 0.1 * (uniqueSources - min));
    const highBonus = highCount > 0 ? 0.15 : 0;
    const confidence = Math.min(0.9, base + extra + highBonus);
    return { verdict: "true", confidence };
  }
  if (uniqueSources === 1) {
    if (highCount === 1) {
      return {
        verdict: "true",
        confidence: 0.6,
        note: "Only one high-quality source available; corroboration advised.",
      };
    }
    const onlyEvidence = evidence[0];
    const confidence = onlyEvidence.source_quality === "low" ? 0.2 : 0.4;
    return {
      verdict: "uncertain",
      confidence,
      note: "Only one medium/low-quality source available.",
    };
  }
  return {
    verdict: "uncertain",
    confidence: 0.45,
    note: "Multiple sources but below corroboration threshold.",
  };
}

type CandidateEntry = {
  value: any;
  evidence: Evidence[];
  bestQuality: number;
  confidence: number;
  verdict: "true" | "mixed" | "uncertain";
};

type FactByKey<K extends BuildingFact["key"]> = Extract<BuildingFact, { key: K }>;

function getFact<K extends BuildingFact["key"]>(
  map: Map<BuildingFact["key"], BuildingFact>,
  key: K
): FactByKey<K> | undefined {
  const fact = map.get(key);
  return fact && fact.key === key ? (fact as FactByKey<K>) : undefined;
}

export async function getBuildingFacts(input: GetBuildingFactsInput): Promise<BuildingFactsResult> {
  const nowIso = input.nowIso ?? viennaNowIso();
  const locale = input.locale ?? "de-AT";
  const lang = (locale.split("-")[0] || locale).toLowerCase();
  const minSources = input.minSourcesPerFact ?? 2;

  const query = { address: input.address, lat: input.lat, lon: input.lon, locale };
  const canonical: BuildingFactsResult["canonical"] = {};
  const notesSet = new Set<string>();
  const candidateMap = new Map<BuildingFact["key"], Map<string, CandidateEntry>>();
  const factVerdicts: Array<{ key: BuildingFact["key"]; verdict: "true" | "mixed" | "uncertain"; confidence: number }> = [];
  const finalFactsMap = new Map<BuildingFact["key"], BuildingFact>();

  function addCandidate(key: BuildingFact["key"], rawValue: any, evidenceInput: Evidence | Evidence[]): void {
    if (rawValue === undefined || rawValue === null) {
      return;
    }
    let value = rawValue;
    if (typeof value === "string") {
      value = value.trim();
      if (!value) {
        return;
      }
    }
    if (key === "levels" || key === "height_m") {
      const numeric = typeof value === "number" ? value : Number.parseFloat(String(value));
      if (!Number.isFinite(numeric)) {
        return;
      }
      value = numeric;
    }
    if (key === "coordinates") {
      const coords = value as { lat: number; lon: number };
      if (!coords || !Number.isFinite(coords.lat) || !Number.isFinite(coords.lon)) {
        return;
      }
      value = { lat: coords.lat, lon: coords.lon };
    }

    const evidences = Array.isArray(evidenceInput) ? evidenceInput : [evidenceInput];
    if (!evidences.length) {
      return;
    }

    const valueKey = factValueKey(key, value);
    let byValue = candidateMap.get(key);
    if (!byValue) {
      byValue = new Map();
      candidateMap.set(key, byValue);
    }
    let entry = byValue.get(valueKey);
    if (!entry) {
      entry = { value, evidence: [], bestQuality: 0, confidence: 0, verdict: "uncertain" };
      byValue.set(valueKey, entry);
    }
    for (const evidence of evidences) {
      entry.evidence.push(evidence);
      const rank = qualityRank(evidence.source_quality);
      if (rank > entry.bestQuality) {
        entry.bestQuality = rank;
        entry.value = value;
      }
    }
  }

  const geocoderProvider = providerOverrides.geocoder ?? new NominatimGeocoder({ userAgent: input.userAgent });
  const overpassProvider = providerOverrides.overpass ?? new OverpassProvider(input.overpassEndpoint, { userAgent: input.userAgent });
  const wikidataProvider = providerOverrides.wikidata ?? new WikidataProvider({ userAgent: input.userAgent });
  const wikipediaProvider =
    providerOverrides.wikipedia ?? new WikipediaProvider({ userAgent: input.userAgent, apiKey: input.wikipediaApiKey });

  let lat = typeof input.lat === "number" ? input.lat : undefined;
  let lon = typeof input.lon === "number" ? input.lon : undefined;
  let resolvedAddress: string | undefined;

  if ((lat === undefined || lon === undefined) && input.address) {
    if (geocoderProvider && typeof geocoderProvider.geocodeAddress === "function") {
      try {
        const geocode = await geocoderProvider.geocodeAddress(input.address);
        if (geocode) {
          lat = geocode.lat;
          lon = geocode.lon;
          if (geocode.displayName) {
            resolvedAddress = geocode.displayName;
            const geoEvidence = makeEvidence({
              title: "Nominatim result",
              url: `https://nominatim.openstreetmap.org/search?format=jsonv2&q=${encodeURIComponent(input.address)}`,
              accessDate: nowIso,
              snippet: `display_name="${geocode.displayName}"`,
            });
            addCandidate("address", geocode.displayName, geoEvidence);
          }
        }
      } catch {
        notesSet.add("Geocoding failed; proceeding with original coordinates if available.");
      }
    }
    if (!resolvedAddress && input.address) {
      resolvedAddress = input.address;
    }
  } else if (input.address) {
    resolvedAddress = input.address;
  }

  if (lat === undefined || lon === undefined) {
    const baseResult: BuildingFactsResult = {
      query,
      canonical: resolvedAddress ? { address: resolvedAddress } : {},
      facts: [],
      verdict: "uncertain",
      confidence: 0,
      notes: Array.from(notesSet).concat("Location could not be resolved; no building facts gathered."),
    };
    return baseResult;
  }

  let overpassFeature: OverpassFeature | null = null;
  const overpassSourceUrl = input.overpassEndpoint ?? "https://overpass-api.de/api/interpreter";
  try {
    if (overpassProvider && typeof overpassProvider.findNearestBuilding === "function") {
      overpassFeature = await overpassProvider.findNearestBuilding(lat, lon);
    }
  } catch {
    notesSet.add("Overpass data unavailable.");
  }

  let wikidataQid: string | undefined;
  let osmWikipedia: { value: string; lang: string; title: string } | null = null;

  if (overpassFeature && overpassFeature.tags) {
    canonical.osm_id = String(overpassFeature.id);
    canonical.osm_type = overpassFeature.type;
    if (typeof overpassFeature.lat === "number" && typeof overpassFeature.lon === "number") {
      lat = overpassFeature.lat;
      lon = overpassFeature.lon;
    }

    const tags = overpassFeature.tags;
    const baseEvidence = (snippet: string) =>
      makeEvidence({
        title: "Overpass building feature",
        url: overpassSourceUrl,
        accessDate: nowIso,
        snippet,
      });

    if (tags.name) {
      addCandidate("name", humanizeTagValue(tags.name), baseEvidence(`name="${tags.name}"`));
    }
    if (tags["addr:full"]) {
      const address = humanizeTagValue(tags["addr:full"]);
      addCandidate("address", address, baseEvidence(`addr:full="${tags["addr:full"]}"`));
      resolvedAddress = resolvedAddress ?? address;
    } else {
      const addressParts: string[] = [];
      if (tags["addr:street"]) {
        let line = humanizeTagValue(tags["addr:street"]);
        if (tags["addr:housenumber"]) {
          line = `${line} ${tags["addr:housenumber"]}`;
        }
        addressParts.push(line);
      }
      const cityParts: string[] = [];
      if (tags["addr:postcode"]) {
        cityParts.push(tags["addr:postcode"]);
      }
      if (tags["addr:city"]) {
        cityParts.push(humanizeTagValue(tags["addr:city"]));
      }
      if (cityParts.length) {
        addressParts.push(cityParts.join(" "));
      }
      if (addressParts.length) {
        const address = addressParts.join(", ");
        addCandidate("address", address, baseEvidence(`addr components from OSM`));
        resolvedAddress = resolvedAddress ?? address;
      }
    }

    const startDate = tags.start_date || tags["construction:start_date"];
    if (startDate) {
      addCandidate("construction_start", normalizeDateValue(startDate), baseEvidence(`start_date=${startDate}`));
    }
    const endDate = tags.end_date || tags["construction:end_date"];
    if (endDate) {
      addCandidate("construction_end", normalizeDateValue(endDate), baseEvidence(`end_date=${endDate}`));
    }
    const levelsRaw = tags["building:levels"];
    if (levelsRaw) {
      const levels = Number.parseFloat(levelsRaw);
      if (Number.isFinite(levels)) {
        addCandidate("levels", levels, baseEvidence(`building:levels=${levelsRaw}`));
      }
    }
    const heightRaw = tags.height;
    if (heightRaw) {
      const height = Number.parseFloat(heightRaw.replace(/,/g, "."));
      if (Number.isFinite(height)) {
        addCandidate("height_m", height, baseEvidence(`height=${heightRaw}`));
      }
    }
    const buildingUse = tags.building;
    if (buildingUse) {
      addCandidate("current_use", humanizeTagValue(buildingUse), baseEvidence(`building=${buildingUse}`));
    }
    const heritageTag = tags.heritage || tags["heritage:designation"] || tags["heritage:operator"];
    if (heritageTag) {
      addCandidate("heritage_designation", humanizeTagValue(heritageTag), baseEvidence(`heritage=${heritageTag}`));
    }
    if (overpassFeature.lat !== undefined && overpassFeature.lon !== undefined) {
      addCandidate(
        "coordinates",
        { lat: overpassFeature.lat, lon: overpassFeature.lon },
        baseEvidence(`center=${overpassFeature.lat.toFixed(5)},${overpassFeature.lon.toFixed(5)}`)
      );
    }
    if (tags.wikidata) {
      const q = tags.wikidata.trim();
      if (q) {
        wikidataQid = q.toUpperCase();
        addCandidate("wikidata_qid", wikidataQid, baseEvidence(`wikidata=${q}`));
      }
    }
    if (tags.wikipedia) {
      osmWikipedia = normalizeWikipediaValue(tags.wikipedia, lang);
      if (osmWikipedia) {
        addCandidate("wikipedia_title", osmWikipedia.value, baseEvidence(`wikipedia=${tags.wikipedia}`));
      }
    }
    addCandidate("osm_id", `${overpassFeature.type}/${overpassFeature.id}`, baseEvidence(`osm_id=${overpassFeature.id}`));
  } else {
    notesSet.add("No building feature found within 60 m of the reference point.");
  }

  if (!wikidataQid && osmWikipedia) {
    try {
      const resolved = await wikidataProvider.resolveFromWikipedia(osmWikipedia.title, osmWikipedia.lang);
      if (resolved) {
        wikidataQid = resolved.toUpperCase();
      }
    } catch {
      notesSet.add("Wikidata lookup from Wikipedia title failed.");
    }
  }

  let wikidataFacts: WikidataFacts | null = null;
  if (wikidataQid) {
    canonical.wikidata_qid = wikidataQid;
    try {
      wikidataFacts = await wikidataProvider.fetchFactsByQid(wikidataQid, lang);
    } catch {
      notesSet.add("Wikidata facts request failed.");
    }
  }

  if (wikidataFacts) {
    const wikidataUrl = wikidataFacts.qid ? `https://www.wikidata.org/wiki/${wikidataFacts.qid}` : "https://www.wikidata.org";
    const baseEvidence = (snippet: string) =>
      makeEvidence({
        title: wikidataFacts?.qid ? `Wikidata ${wikidataFacts.qid}` : "Wikidata entity",
        url: wikidataUrl,
        accessDate: nowIso,
        snippet,
      });
    if (wikidataFacts.qid) {
      addCandidate("wikidata_qid", wikidataFacts.qid.toUpperCase(), baseEvidence(`entity=${wikidataFacts.qid}`));
    }
    if (wikidataFacts.label) {
      addCandidate("name", wikidataFacts.label, baseEvidence(`label=${wikidataFacts.label}`));
    }
    if (wikidataFacts.inception) {
      const inception = normalizeDateValue(wikidataFacts.inception);
      addCandidate("construction_start", inception, baseEvidence(`P571 inception ${inception}`));
    }
    if (wikidataFacts.architect) {
      addCandidate("architect", wikidataFacts.architect, baseEvidence(`P84 architect ${wikidataFacts.architect}`));
    }
    if (wikidataFacts.style) {
      addCandidate("architectural_style", wikidataFacts.style, baseEvidence(`P149 style ${wikidataFacts.style}`));
    }
    if (wikidataFacts.heritage) {
      addCandidate("heritage_designation", wikidataFacts.heritage, baseEvidence(`P1435 heritage ${wikidataFacts.heritage}`));
    }
    if (wikidataFacts.coords) {
      addCandidate(
        "coordinates",
        { lat: wikidataFacts.coords.lat, lon: wikidataFacts.coords.lon },
        baseEvidence(`P625 coordinates ${wikidataFacts.coords.lat.toFixed(5)},${wikidataFacts.coords.lon.toFixed(5)}`)
      );
    }
    if (wikidataFacts.wikipediaTitle) {
      const wiki = normalizeWikipediaValue(`${lang}:${wikidataFacts.wikipediaTitle}`, lang);
      if (wiki) {
        addCandidate("wikipedia_title", wiki.value, baseEvidence(`sitelink ${wiki.value}`));
      }
    }
  }

  let wikipediaSummary: WikipediaSummary | null = null;
  let attemptedWikipediaSummary = false;
  if (wikipediaProvider && typeof wikipediaProvider.fetchSummary === "function") {
    const attempts: Array<{ title: string; lang: string }> = [];
    if (wikidataFacts?.wikipediaTitle) {
      attempts.push({ title: wikidataFacts.wikipediaTitle, lang });
    }
    if (osmWikipedia) {
      attempts.push({ title: osmWikipedia.title, lang: osmWikipedia.lang });
    }
    const seen = new Set<string>();
    for (const attempt of attempts) {
      const attemptKey = `${attempt.lang}:${attempt.title}`.toLowerCase();
      if (seen.has(attemptKey)) {
        continue;
      }
      seen.add(attemptKey);
      attemptedWikipediaSummary = true;
      try {
        const summary = await wikipediaProvider.fetchSummary(attempt.title, attempt.lang);
        if (summary) {
          wikipediaSummary = summary;
          break;
        }
      } catch {
        attemptedWikipediaSummary = true;
      }
    }
  }
  if (!wikipediaSummary && attemptedWikipediaSummary) {
    notesSet.add("Wikipedia summary could not be retrieved for the linked article.");
  }
  if (wikipediaSummary) {
    const summaryText = wikipediaSummary.extract || wikipediaSummary.description;
    const trimmedSummary = summaryText ? trimWords(summaryText, 80) : null;
    const evidence = makeEvidence({
      title: wikipediaSummary.title,
      url: wikipediaSummary.url,
      accessDate: nowIso,
      snippet: summaryText || undefined,
      publishDate: wikipediaSummary.lastModified ?? null,
      qualityOverride: "medium",
      whyOverride: "Wikipedia article summary (community-maintained).",
    });
    const normalized = normalizeWikipediaValue(
      `${wikipediaSummary.lang}:${wikipediaSummary.normalizedTitle}`,
      wikipediaSummary.lang
    );
    if (normalized) {
      addCandidate("wikipedia_title", normalized.value, evidence);
    }
    if (trimmedSummary) {
      addCandidate("wikipedia_summary", trimmedSummary, evidence);
    }
  }

  const notesBeforeFacts = notesSet.size;
  let hasConflict = false;

  for (const [key, valueMap] of candidateMap.entries()) {
    const entries = Array.from(valueMap.values());
    for (const entry of entries) {
      entry.evidence = mergeEvidence(entry.evidence);
      const verdictInfo = pickVerdict(entry.evidence, minSources);
      entry.confidence = verdictInfo.confidence;
      entry.verdict = verdictInfo.verdict;
      factVerdicts.push({ key, verdict: verdictInfo.verdict, confidence: verdictInfo.confidence });
      if (verdictInfo.note) {
        notesSet.add(`${key}: ${verdictInfo.note}`);
      }
    }
    if (entries.length > 1) {
      hasConflict = true;
      const valuesForNote = entries.map((entry) => formatValueForNote(key, entry.value));
      notesSet.add(`${key}: conflicting values ${valuesForNote.join(" vs ")}`);
    }
    entries.sort((a, b) => {
      if (b.confidence !== a.confidence) {
        return b.confidence - a.confidence;
      }
      return b.bestQuality - a.bestQuality;
    });
    const best = entries[0];
    if (!best || !best.evidence.length) {
      continue;
    }
    const fact = buildFact(key, best.value, best.evidence, best.confidence);
    if (fact) {
      finalFactsMap.set(key, fact);
    }
  }

  const facts = Array.from(finalFactsMap.values());
  facts.sort((a, b) => {
    if (b.confidence !== a.confidence) {
      return b.confidence - a.confidence;
    }
    return a.key.localeCompare(b.key);
  });

  const coordsFact = getFact(finalFactsMap, "coordinates");
  if (coordsFact) {
    canonical.lat = coordsFact.value.lat;
    canonical.lon = coordsFact.value.lon;
  } else if (typeof lat === "number" && typeof lon === "number") {
    canonical.lat = lat;
    canonical.lon = lon;
  }

  const addressFact = getFact(finalFactsMap, "address");
  if (addressFact) {
    canonical.address = addressFact.value;
  } else if (resolvedAddress) {
    canonical.address = resolvedAddress;
  }

  const wikidataFact = getFact(finalFactsMap, "wikidata_qid");
  if (wikidataFact) {
    canonical.wikidata_qid = wikidataFact.value;
  }
  const wikipediaFact = getFact(finalFactsMap, "wikipedia_title");
  if (wikipediaFact) {
    canonical.wikipedia_title = wikipediaFact.value;
  } else if (osmWikipedia) {
    canonical.wikipedia_title = osmWikipedia.value;
  }

  if (!canonical.osm_id && overpassFeature) {
    canonical.osm_id = String(overpassFeature.id);
    canonical.osm_type = overpassFeature.type;
  }

  let hasTrue = false;
  let hasUncertain = false;
  let hasMixedVerdict = false;
  for (const record of factVerdicts) {
    if (record.verdict === "true") {
      hasTrue = true;
    } else if (record.verdict === "mixed") {
      hasMixedVerdict = true;
    } else {
      hasUncertain = true;
    }
  }

  let verdict: "true" | "mixed" | "uncertain";
  if (hasMixedVerdict || hasConflict) {
    verdict = "mixed";
  } else if (hasTrue && hasUncertain) {
    verdict = "mixed";
  } else if (hasTrue) {
    verdict = "true";
  } else {
    verdict = "uncertain";
  }

  let confidence = facts.length
    ? facts.reduce((sum, fact) => sum + fact.confidence, 0) / facts.length
    : 0;
  if (verdict === "uncertain") {
    confidence = Math.min(confidence, 0.5);
  } else if (verdict === "mixed") {
    confidence = Math.min(confidence, 0.65);
  }
  confidence = Math.round(confidence * 1000) / 1000;

  const summary = buildSummary(facts, { maxWords: 120 });

  const notesArray = Array.from(notesSet);
  if (!notesArray.length && notesBeforeFacts === 0 && verdict === "mixed") {
    notesArray.push("Conflicting evidence prevented a clear verdict.");
  }

  const result: BuildingFactsResult = {
    query,
    canonical,
    summary: summary || undefined,
    facts,
    verdict,
    confidence,
    notes: notesArray.length ? notesArray : undefined,
  };

  return result;
}

function buildFact(
  key: BuildingFact["key"],
  value: any,
  evidence: Evidence[],
  confidence: number
): BuildingFact | null {
  switch (key) {
    case "name":
      return { key: "name", value: String(value), evidence, confidence };
    case "address":
      return { key: "address", value: String(value), evidence, confidence };
    case "wikidata_qid":
      return { key: "wikidata_qid", value: String(value), evidence, confidence };
    case "wikipedia_title":
      return { key: "wikipedia_title", value: String(value), evidence, confidence };
    case "wikipedia_summary":
      return { key: "wikipedia_summary", value: String(value), evidence, confidence };
    case "construction_start":
      return { key: "construction_start", value: String(value), evidence, confidence };
    case "construction_end":
      return { key: "construction_end", value: String(value), evidence, confidence };
    case "architect":
      return { key: "architect", value: String(value), evidence, confidence };
    case "architectural_style":
      return { key: "architectural_style", value: String(value), evidence, confidence };
    case "heritage_designation":
      return { key: "heritage_designation", value: String(value), evidence, confidence };
    case "current_use":
      return { key: "current_use", value: String(value), evidence, confidence };
    case "historic_use":
      return { key: "historic_use", value: String(value), evidence, confidence };
    case "notable_event":
      return { key: "notable_event", value: String(value), evidence, confidence };
    case "osm_id":
      return { key: "osm_id", value: String(value), evidence, confidence };
    case "coordinates":
      return { key: "coordinates", value: value as { lat: number; lon: number }, evidence, confidence };
    case "levels":
      return { key: "levels", value: Number(value), evidence, confidence };
    case "height_m":
      return { key: "height_m", value: Number(value), evidence, confidence };
    default:
      return null;
  }
}

function factValueKey(key: BuildingFact["key"], value: any): string {
  switch (key) {
    case "coordinates":
      return `${(value as { lat: number }).lat.toFixed(5)},${(value as { lon: number }).lon.toFixed(5)}`;
    case "levels":
    case "height_m":
      return String(value);
    case "wikidata_qid":
      return typeof value === "string" ? value.trim().toUpperCase() : String(value);
    case "construction_start":
    case "construction_end":
      return normalizeDateKey(String(value));
    default:
      return typeof value === "string" ? value.trim().toLowerCase() : String(value);
  }
}

function normalizeDateKey(value: string): string {
  const match = value.trim().match(/(-?\d{4})/);
  return match ? match[1] : value.trim().toLowerCase();
}

function formatValueForNote(key: BuildingFact["key"], value: any): string {
  if (key === "coordinates" && value) {
    const coords = value as { lat: number; lon: number };
    return `${coords.lat.toFixed(4)}, ${coords.lon.toFixed(4)}`;
  }
  if (typeof value === "object" && value !== null) {
    return JSON.stringify(value);
  }
  return String(value);
}

function formatDateForSentence(value: string): string {
  const trimmed = value.trim();
  const match = trimmed.match(/^(-?\d{4})(?:-(\d{2}))?(?:-(\d{2}))?/);
  if (match) {
    const parts = [match[1]];
    if (match[2]) {
      parts.push(match[2]);
    }
    if (match[3]) {
      parts.push(match[3]);
    }
    return parts.join("-");
  }
  return trimmed;
}

function buildSummary(facts: BuildingFact[], opts: { maxWords: number }): string {
  const factMap = new Map<BuildingFact["key"], BuildingFact>();
  for (const fact of facts) {
    factMap.set(fact.key, fact);
  }
  const pieces: string[] = [];
  const nameFact = getFact(factMap, "name");
  const addressFact = getFact(factMap, "address");
  const coordsFact = getFact(factMap, "coordinates");
  const startFact = getFact(factMap, "construction_start");
  const architectFact = getFact(factMap, "architect");
  const styleFact = getFact(factMap, "architectural_style");
  const heritageFact = getFact(factMap, "heritage_designation");
  const currentUseFact = getFact(factMap, "current_use");

  const isStrong = (fact?: BuildingFact) => (fact ? fact.confidence >= 0.6 : false);

  if (isStrong(nameFact)) {
    let sentence = `${nameFact!.value} is documented`;
    if (isStrong(addressFact)) {
      sentence += ` at ${addressFact!.value}`;
    } else if (isStrong(coordsFact)) {
      const coords = coordsFact!.value;
      sentence += ` near ${coords.lat.toFixed(3)}°, ${coords.lon.toFixed(3)}°`;
    }
    sentence += ".";
    pieces.push(sentence);
  } else if (isStrong(addressFact) && isStrong(coordsFact)) {
    const coords = coordsFact!.value;
    pieces.push(`The building at ${addressFact!.value} is recorded near ${coords.lat.toFixed(3)}°, ${coords.lon.toFixed(3)}°.`);
  } else if (isStrong(coordsFact)) {
    const coords = coordsFact!.value;
    pieces.push(`A mapped building is located near ${coords.lat.toFixed(3)}°, ${coords.lon.toFixed(3)}°.`);
  }

  if (isStrong(startFact)) {
    pieces.push(`Construction is recorded around ${formatDateForSentence(startFact!.value)}.`);
  }
  if (isStrong(architectFact)) {
    pieces.push(`Architect: ${architectFact!.value}.`);
  }
  if (isStrong(styleFact)) {
    pieces.push(`Style noted as ${styleFact!.value}.`);
  }
  if (isStrong(heritageFact)) {
    pieces.push(`Heritage status: ${heritageFact!.value}.`);
  }
  if (isStrong(currentUseFact)) {
    pieces.push(`Current use reported as ${currentUseFact!.value}.`);
  }

  if (!pieces.length) {
    return "";
  }
  return trimWords(pieces.join(" "), opts.maxWords);
}

function normalizeDateValue(raw: string): string {
  const trimmed = raw.trim();
  if (/c\.?|circa|approx/i.test(trimmed)) {
    return trimmed;
  }
  const match = trimmed.match(/^(-?\d{4})(?:-(\d{2}))?(?:-(\d{2}))?/);
  if (match) {
    const parts = [match[1]];
    if (match[2]) {
      parts.push(match[2]);
    }
    if (match[3]) {
      parts.push(match[3]);
    }
    return parts.join("-");
  }
  if (trimmed.includes("T")) {
    const datePart = trimmed.split("T")[0];
    if (datePart.match(/^(-?\d{4})(-\d{2}){0,2}$/)) {
      return datePart;
    }
  }
  return trimmed.replace(/Z$/, "");
}

function normalizeWikipediaValue(raw: string, fallbackLang: string): { value: string; lang: string; title: string } | null {
  const trimmed = raw.trim();
  if (!trimmed) {
    return null;
  }
  const parts = trimmed.split(":");
  let lang = fallbackLang.toLowerCase();
  let title = trimmed;
  if (parts.length > 1) {
    lang = parts.shift()!.toLowerCase();
    title = parts.join(":");
  }
  title = title.replace(/_/g, " ").trim();
  if (!title) {
    return null;
  }
  return { value: `${lang}:${title}`, lang, title };
}

function humanizeTagValue(value: string): string {
  return value.replace(/_/g, " ").trim();
}

type MakeEvidenceInput = {
  title: string;
  url: string;
  accessDate: string;
  snippet?: string;
  publishDate?: string | null;
  qualityOverride?: SourceQuality;
  whyOverride?: string;
};

function makeEvidence(params: MakeEvidenceInput): Evidence {
  const { q, why } = qualityFor(params.url);
  return {
    title: params.title,
    url: params.url,
    publish_date: params.publishDate,
    access_date: params.accessDate,
    snippet: params.snippet ? trimWords(params.snippet, 25) : undefined,
    source_quality: params.qualityOverride ?? q,
    why_trustworthy: params.whyOverride ?? why,
  };
}
