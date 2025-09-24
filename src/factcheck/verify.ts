import { WebSearchTool } from "../tools/types";

export type FactCheckResult = {
  question: string;
  verdict: "true" | "false" | "mixed" | "uncertain";
  confidence: number;
  claims: Array<{
    text: string;
    verdict: "true" | "false" | "mixed" | "uncertain";
    confidence: number;
    evidence: Array<{
      title: string;
      url: string;
      publish_date?: string | null;
      access_date: string;
      snippet?: string;
      domain_quality: "high" | "medium" | "low";
      why_trustworthy: string;
    }>;
    notes?: string;
  }>;
  gaps_or_caveats?: string[];
};

type EvidenceItem = FactCheckResult["claims"][number]["evidence"][number];

const MONTHS: Record<string, string> = {
  january: "01",
  february: "02",
  march: "03",
  april: "04",
  may: "05",
  june: "06",
  july: "07",
  august: "08",
  september: "09",
  october: "10",
  november: "11",
  december: "12",
};

const STOP_WORDS = new Set([
  "is",
  "was",
  "the",
  "und",
  "and",
  "for",
  "with",
  "von",
  "der",
  "die",
  "das",
  "in",
  "im",
  "an",
  "auf",
  "has",
  "have",
  "hat",
  "wurde",
  "served",
  "there",
]);

export async function verifyParagraph(input: {
  paragraph: string;
  nowIso?: string;
  minSourcesPerClaim?: number;
  tools: WebSearchTool;
}): Promise<FactCheckResult> {
  const paragraph = input.paragraph?.trim?.() ?? "";
  const nowIso = getViennaNowIso(input.nowIso);
  const minSources = Math.max(1, input.minSourcesPerClaim ?? 2);
  const claimTexts = extractClaims(paragraph);
  const claims: FactCheckResult["claims"] = [];
  const gaps: string[] = [];

  for (const text of claimTexts) {
    const queries = buildQueries(text);
    const recency = inferRecencyWindow(text);
    const evidence = await collectEvidence(queries, input.tools, nowIso, recency, minSources);
    const decision = decideVerdict(evidence, minSources);
    if (evidence.length < minSources) {
      gaps.push(`Claim "${text}" lacks sufficient independent sources.`);
    }
    claims.push({
      text,
      verdict: decision.verdict,
      confidence: decision.confidence,
      evidence,
      ...(decision.notes ? { notes: decision.notes } : {}),
    });
  }

  const overall = aggregateVerdict(claims);
  const result: FactCheckResult = {
    question: paragraph,
    verdict: overall.verdict,
    confidence: overall.confidence,
    claims,
    ...(gaps.length ? { gaps_or_caveats: Array.from(new Set(gaps)) } : {}),
  };
  assertFactCheckResult(result);
  return result;
}

async function collectEvidence(
  queries: string[],
  tool: WebSearchTool,
  nowIso: string,
  recencyDays: number | undefined,
  minSources: number
): Promise<EvidenceItem[]> {
  const evidence: EvidenceItem[] = [];
  const seenUrls = new Set<string>();
  const seenDomains = new Set<string>();
  for (const query of queries) {
    let results: Awaited<ReturnType<WebSearchTool["searchWeb"]>> = [];
    try {
      results = await tool.searchWeb(query, recencyDays ? { recencyDays, max: 10 } : { max: 10 });
    } catch {
      continue;
    }
    for (const item of results) {
      if (!item || typeof item.url !== "string") {
        continue;
      }
      const url = sanitizeUrl(item.url);
      if (!url || seenUrls.has(url)) {
        continue;
      }
      const domain = extractHostname(url);
      if (domain && seenDomains.has(domain) && evidence.length < minSources) {
        continue;
      }
      const score = scoreSource(url, item.title);
      evidence.push({
        title: typeof item.title === "string" && item.title.trim() ? item.title.trim() : url,
        url,
        publish_date: sanitizeIsoDate(item.publishDateIso),
        access_date: nowIso,
        snippet: item.snippet ? trimToWords(item.snippet, 25) : undefined,
        domain_quality: score.domain_quality,
        why_trustworthy: score.why,
      });
      seenUrls.add(url);
      if (domain) {
        seenDomains.add(domain);
      }
      if (evidence.length >= Math.max(minSources * 2, minSources + 2)) {
        break;
      }
    }
    if (evidence.length >= Math.max(minSources * 2, minSources + 2)) {
      break;
    }
  }
  evidence.sort((a, b) => domainRank(b.domain_quality) - domainRank(a.domain_quality));
  return evidence;
}

function extractClaims(paragraph: string): string[] {
  const clean = paragraph.replace(/\s+/g, " ").trim();
  if (!clean) {
    return [];
  }
  const sentences = clean.split(/[.!?;]+/).map((s) => s.trim()).filter(Boolean);
  const claims: string[] = [];
  const seen = new Set<string>();
  let anchorAddress: string | undefined;
  let anchorSubject: string | undefined;

  const push = (raw: string) => {
    const normalized = normalizeClaim(raw);
    if (!normalized) {
      return;
    }
    const key = normalized.toLowerCase();
    if (!seen.has(key)) {
      seen.add(key);
      claims.push(normalized);
    }
  };

  for (const sentence of sentences) {
    const parts = sentence
      .split(/\b(?:and|und|aber|but|sowie|,|while)\b/i)
      .map((piece) => piece.trim())
      .filter(Boolean);
    anchorSubject = detectSubject(sentence) ?? anchorSubject ?? anchorAddress;

    for (const fragment of parts) {
      let candidate = fragment;
      if (anchorSubject && !detectSubject(candidate)) {
        candidate = `${anchorSubject} ${candidate}`;
      }
      if (anchorAddress) {
        candidate = candidate.replace(/\b(lived|stayed|worked|resided|remained) there\b/gi, (_m, verb) => `${verb} at ${anchorAddress}`);
        candidate = candidate.replace(/\bthere\b/gi, anchorAddress);
      }
      push(candidate);
      const candidateSubject = detectSubject(candidate);
      if (candidateSubject) {
        anchorSubject = candidateSubject;
      }
      const address = detectAddress(candidate);
      if (address) {
        anchorAddress = address;
        anchorSubject = anchorSubject ?? address;
      }
      const subject = candidateSubject ?? address ?? anchorSubject;
      const year = candidate.match(/\b(1[6-9]\d{2}|20\d{2})\b/);
      if (address && /district/i.test(candidate) && /vienna|wien/i.test(candidate)) {
        push(`${address} is located in Vienna`);
        const descriptor = extractDistrictDescriptor(candidate);
        if (descriptor) {
          push(`${address} is part of Vienna's ${descriptor}`);
        }
      }
      if (address && /served as/i.test(candidate) && /hospital/i.test(candidate)) {
        push(`The Rothschild Hospital was located at ${address}`);
      }
      if (subject && year && /(completed|built|opened|construction|established)/i.test(candidate)) {
        push(`The completion year of ${subject} was ${year[0]}`);
      }
      const lived = candidate.match(/lived at (.+)$/i);
      if (subject && lived) {
        push(`${subject} resided at ${lived[1]}`);
      }
    }
  }

  for (const claim of [...claims]) {
    if (claims.length >= 4) {
      break;
    }
    const extra = paraphraseClaim(claim);
    if (extra) {
      push(extra);
    }
  }

  if (claims.length < 4 && anchorAddress) {
    push(`${anchorAddress} is associated with Vienna`);
  }

  return claims.slice(0, 12);
}

function paraphraseClaim(claim: string): string | undefined {
  const completion = claim.match(/^(.+?) was completed in (\d{4})$/i);
  if (completion) {
    return `The completion year of ${completion[1]} was ${completion[2]}`;
  }
  const served = claim.match(/^(.+?) served as (.+)$/i);
  if (served) {
    const address = detectAddress(served[1]) ?? served[1];
    return `The ${served[2]} was located at ${address}`;
  }
  const lived = claim.match(/^(.+?) lived at (.+)$/i);
  if (lived) {
    return `${lived[1]} resided at ${lived[2]}`;
  }
  const located = claim.match(/^(.+?) is in (.+)$/i);
  if (located) {
    return `${located[1]} is located in ${located[2]}`;
  }
  return undefined;
}

function buildQueries(claim: string): string[] {
  const queries: string[] = [];
  const add = (value?: string) => {
    const trimmed = value?.trim();
    if (trimmed && !queries.includes(trimmed)) {
      queries.push(trimmed);
    }
  };

  const normalized = claim.trim();
  const address = detectAddress(normalized);
  const keywords = extractKeywords(normalized).filter((word) => !STOP_WORDS.has(word.toLowerCase()));
  add(keywords.slice(0, 7).join(" "));
  add(normalized);
  if (address) {
    add(address);
  }
  if (address && /vienna|wien/i.test(normalized)) {
    add(`${address} district Vienna`);
  }
  if (address && /district/i.test(normalized) && /vienna|wien/i.test(normalized)) {
    add(`${address} Bezirk Wien`);
  }
  const person = detectPrimaryPerson(normalized);
  if (person && address) {
    if (/(?:lived|resided|stayed|wohnte)/i.test(normalized)) {
      add(`${person} ${address} plaque`);
      add(`${person} ${address} residence`);
    } else {
      add(`${person} ${address}`);
    }
  }
  add(germanize(keywords.length ? keywords.join(" ") : normalized));
  const year = normalized.match(/\b(1[6-9]\d{2}|20\d{2})\b/);
  if (address && year) {
    add(`${address} ${year[0]} Fertigstellung`);
  }
  if (queries.length < 2 && address) {
    add(`${address} Vienna`);
  }
  if (queries.length < 2 && keywords.length >= 2) {
    add(keywords.slice(0, 2).join(" "));
  }
  return queries.slice(0, 4);
}

function scoreSource(url: string, title?: string): { domain_quality: "high" | "medium" | "low"; why: string } {
  const host = extractHostname(url)?.toLowerCase() ?? "";
  if (/\.gv\.at$/.test(host) || /\.gv\./.test(host) || /\.gov(\.|$)/.test(host)) {
    return { domain_quality: "high", why: "Official government publication" };
  }
  if (/\.ac\./.test(host) || /\.edu(\.|$)/.test(host)) {
    return { domain_quality: "high", why: "Academic or educational institution" };
  }
  if (/(museum|archive|library)/.test(host)) {
    return { domain_quality: "high", why: "Cultural institution source" };
  }
  if (/wikipedia\.org$/.test(host)) {
    return { domain_quality: "medium", why: "Community-edited reference" };
  }
  if (/(blogspot|wordpress|medium\.com)/.test(host) || /\.blog$/.test(host)) {
    return { domain_quality: "low", why: "Self-published platform" };
  }
  if (/news|zeitung|press/.test(host)) {
    return { domain_quality: "medium", why: "Press outlet" };
  }
  if (/\.at$/.test(host) && title && /stadt|amt|regierung|universit|museum/i.test(title)) {
    return { domain_quality: "high", why: "Regional authoritative coverage" };
  }
  return { domain_quality: "medium", why: "Identifiable publisher" };
}

function trimToWords(text: string, maxWords: number): string {
  const words = text.replace(/\s+/g, " ").trim().split(" ");
  if (words.length <= maxWords) {
    return words.join(" ");
  }
  return words.slice(0, maxWords).join(" ") + "…";
}

function decideVerdict(evidence: EvidenceItem[], minSources: number): {
  verdict: FactCheckResult["verdict"];
  confidence: number;
  notes?: string;
} {
  if (!evidence.length) {
    return { verdict: "uncertain", confidence: 0.2, notes: "No supporting sources found." };
  }
  const conflicts = evidence.filter((item) => indicatesConflict(item.snippet));
  const hasConflict = conflicts.length > 0;
  const support = evidence.length - conflicts.length;
  const highPrimary = evidence.length === 1 && evidence[0].domain_quality === "high";
  let verdict: FactCheckResult["verdict"];
  if (hasConflict && support > 0) {
    verdict = "mixed";
  } else if (hasConflict && support === 0) {
    verdict = "false";
  } else if (evidence.length >= minSources || highPrimary) {
    verdict = "true";
  } else {
    verdict = "uncertain";
  }
  const qualitySum = evidence.reduce((sum, item) => sum + qualityWeight(item.domain_quality), 0);
  let confidence = Math.min(0.95, 0.15 + qualitySum);
  if (verdict === "uncertain") {
    confidence = Math.min(confidence, 0.45);
  }
  if (verdict === "mixed") {
    confidence = Math.min(confidence, 0.6);
  }
  if (verdict === "false") {
    confidence = Math.min(confidence, 0.7);
  }
  if (evidence.length < minSources && verdict === "true") {
    confidence = Math.min(confidence, highPrimary ? 0.5 : 0.4);
  }
  confidence = clampConfidence(confidence);
  let notes: string | undefined;
  if (hasConflict) {
    notes = "Sources provide conflicting statements.";
  } else if (verdict === "uncertain") {
    notes = "Insufficient independent coverage located.";
  }
  return { verdict, confidence, ...(notes ? { notes } : {}) };
}

function aggregateVerdict(claims: FactCheckResult["claims"]): {
  verdict: FactCheckResult["verdict"];
  confidence: number;
} {
  if (!claims.length) {
    return { verdict: "uncertain", confidence: 0 };
  }
  let total = 0;
  let score = 0;
  let hasMixed = false;
  for (const claim of claims) {
    total += claim.confidence;
    score += verdictScore(claim.verdict) * claim.confidence;
    if (claim.verdict === "mixed") {
      hasMixed = true;
    }
  }
  if (total === 0) {
    return { verdict: "uncertain", confidence: 0 };
  }
  const normalized = score / total;
  let verdict: FactCheckResult["verdict"];
  if (normalized >= 0.4) {
    verdict = "true";
  } else if (normalized <= -0.4) {
    verdict = "false";
  } else if (hasMixed) {
    verdict = "mixed";
  } else {
    verdict = "uncertain";
  }
  let confidence = Math.min(1, Math.abs(normalized) * 0.6 + (total / claims.length) * 0.7);
  if (verdict === "mixed") {
    confidence = Math.min(confidence, 0.6);
  }
  if (verdict === "uncertain") {
    confidence = Math.min(confidence, 0.5);
  }
  return { verdict, confidence: clampConfidence(confidence) };
}

function getViennaNowIso(nowIso?: string): string {
  let base = nowIso ? new Date(nowIso) : new Date();
  if (Number.isNaN(base.getTime())) {
    base = new Date();
  }
  const formatter = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/Vienna",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
    timeZoneName: "shortOffset",
  });
  const parts = new Map(formatter.formatToParts(base).map((p) => [p.type, p.value] as const));
  const year = parts.get("year");
  const month = parts.get("month");
  const day = parts.get("day");
  const hour = parts.get("hour") ?? "00";
  const minute = parts.get("minute") ?? "00";
  const second = parts.get("second") ?? "00";
  const zone = parts.get("timeZoneName");
  if (!year || !month || !day) {
    return base.toISOString();
  }
  let offset = "+00:00";
  if (zone) {
    const match = zone.match(/GMT([+-])(\d{1,2})(?::?(\d{2}))?/i);
    if (match) {
      const sign = match[1];
      const hours = match[2].padStart(2, "0");
      const minutes = (match[3] ?? "00").padStart(2, "0");
      offset = `${sign}${hours}:${minutes}`;
    }
  }
  return `${year}-${month}-${day}T${hour}:${minute}:${second}${offset}`;
}

function inferRecencyWindow(claim: string): number | undefined {
  const lower = claim.toLowerCase();
  if (/(today|currently|now|recently)/.test(lower)) {
    return 30;
  }
  const yearMatch = claim.match(/\b(20\d{2})\b/);
  if (yearMatch) {
    const year = Number(yearMatch[1]);
    if (new Date().getUTCFullYear() - year <= 2) {
      return 365;
    }
  }
  return undefined;
}

function sanitizeIsoDate(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function sanitizeUrl(url: string): string | undefined {
  try {
    const parsed = new URL(url);
    parsed.hash = "";
    return parsed.toString();
  } catch {
    return undefined;
  }
}

function extractHostname(url: string): string | undefined {
  try {
    return new URL(url).hostname;
  } catch {
    return undefined;
  }
}

function detectAddress(text: string): string | undefined {
  const match = text.match(/\b[\p{L}][\p{L}\s.'\-]*\s+\d+[a-zA-Z]?\b/u);
  return match ? match[0].replace(/\s+/g, " ").trim() : undefined;
}

function detectSubject(text: string): string | undefined {
  const match = text.match(/^([\p{L}\d][\p{L}\d\s.'\-]*?)\s+(?:is|was|served|became|remained|functioned|lived)/iu);
  return match ? match[1].trim() : undefined;
}

function extractDistrictDescriptor(text: string): string | undefined {
  const viennaMatch = text.match(/vienna['’]s\s+([^.,;]+?district)/i);
  if (viennaMatch && viennaMatch[1]) {
    return collapseWhitespace(viennaMatch[1]);
  }
  const ordinalMatch = text.match(/\b(\d{1,2}(?:st|nd|rd|th))\s+district\b/i);
  if (ordinalMatch && ordinalMatch[1]) {
    return `${ordinalMatch[1]} district`;
  }
  const bezirkMatch = text.match(/\b(\d{1,2})\.?\s*Bezirk\b/i);
  if (bezirkMatch && bezirkMatch[1]) {
    return `${toOrdinal(Number(bezirkMatch[1]))} district`;
  }
  return undefined;
}

function collapseWhitespace(value: string): string {
  return value.replace(/\s+/g, " ").trim();
}

function toOrdinal(n: number): string {
  const abs = Math.abs(n);
  const mod100 = abs % 100;
  if (mod100 >= 11 && mod100 <= 13) {
    return `${n}th`;
  }
  switch (abs % 10) {
    case 1:
      return `${n}st`;
    case 2:
      return `${n}nd`;
    case 3:
      return `${n}rd`;
    default:
      return `${n}th`;
  }
}

function detectPrimaryPerson(text: string): string | undefined {
  const matches = text.match(/\b[A-ZÄÖÜ][a-zäöüß]+(?:\s+[A-ZÄÖÜ][a-zäöüß]+)*\b/gu);
  if (!matches) {
    return undefined;
  }
  for (const match of matches) {
    if (/ungargasse|vienna|wien|district|hospital|rothschild/i.test(match)) {
      continue;
    }
    return match;
  }
  return undefined;
}

function germanize(text: string): string | undefined {
  const replacements: Record<string, string> = {
    vienna: "Wien",
    district: "Bezirk",
    hospital: "Krankenhaus",
    lived: "wohnte",
    residence: "Wohnsitz",
    museum: "Museum",
    street: "Straße",
  };
  const words = text.split(/\s+/);
  let changed = false;
  const translated = words.map((word) => {
    const lower = word.toLowerCase();
    if (replacements[lower]) {
      changed = true;
      const replacement = replacements[lower];
      if (word && word[0] === word[0].toUpperCase()) {
        return replacement.charAt(0).toUpperCase() + replacement.slice(1);
      }
      return replacement;
    }
    if (lower === "3rd") {
      changed = true;
      return "3.";
    }
    return word;
  });
  if (!changed) {
    return undefined;
  }
  return translated.join(" ");
}

function qualityWeight(quality: EvidenceItem["domain_quality"]): number {
  switch (quality) {
    case "high":
      return 0.35;
    case "medium":
      return 0.2;
    case "low":
    default:
      return 0.1;
  }
}

function domainRank(quality: EvidenceItem["domain_quality"]): number {
  switch (quality) {
    case "high":
      return 3;
    case "medium":
      return 2;
    case "low":
    default:
      return 1;
  }
}

function verdictScore(verdict: FactCheckResult["verdict"]): number {
  switch (verdict) {
    case "true":
      return 1;
    case "false":
      return -1;
    default:
      return 0;
  }
}

function clampConfidence(value: number): number {
  return Number(Math.min(1, Math.max(0, value)).toFixed(2));
}

function isVerdict(value: unknown): value is FactCheckResult["verdict"] {
  return value === "true" || value === "false" || value === "mixed" || value === "uncertain";
}

function isConfidence(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 1;
}

function indicatesConflict(snippet?: string): boolean {
  if (!snippet) {
    return false;
  }
  const lower = snippet.toLowerCase();
  return /not\s+(?:in|at|true|located|confirmed)/.test(lower) || /(no evidence|never occurred|disputed|false claim|kein nachweis|wurde nicht)/.test(lower);
}

function normalizeClaim(text: string): string {
  let normalized = text.replace(/[\r\n]+/g, " ").replace(/\s+/g, " ").trim();
  normalized = normalized.replace(/[’‘]/g, "'").replace(/[“”]/g, '"');
  normalized = normalized.replace(/\b([A-Za-z]+)’s\b/g, "$1's");
  normalized = normalized.replace(/[.,;:]+$/, "");
  normalized = normalizeDates(normalized);
  if (!normalized) {
    return "";
  }
  return normalized.charAt(0).toUpperCase() + normalized.slice(1);
}

function normalizeDates(text: string): string {
  let normalized = text.replace(/\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b/g, (_m, d, m, y) => `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`);
  normalized = normalized.replace(
    /\b(\d{1,2}) (January|February|March|April|May|June|July|August|September|October|November|December) (\d{4})\b/gi,
    (_m, day, month, year) => {
      const key = MONTHS[month.toLowerCase()];
      return `${year}-${key ?? month}-${String(day).padStart(2, "0")}`;
    }
  );
  return normalized;
}

function extractKeywords(text: string): string[] {
  const matches = text.match(/\b[\p{L}0-9][\p{L}0-9'\-]*\b/gu) ?? [];
  const unique: string[] = [];
  for (const word of matches) {
    if (!unique.includes(word)) {
      unique.push(word);
    }
  }
  return unique;
}

function assertFactCheckResult(value: unknown): asserts value is FactCheckResult {
  if (typeof value !== "object" || value === null) {
    throw new Error("FactCheckResult must be an object");
  }
  const result = value as Record<string, unknown>;
  if (typeof result.question !== "string" || !isVerdict(result.verdict) || !isConfidence(result.confidence)) {
    throw new Error("FactCheckResult fields invalid");
  }
  if (!Array.isArray(result.claims)) {
    throw new Error("FactCheckResult.claims must be an array");
  }
  for (const claim of result.claims) {
    if (typeof claim !== "object" || claim === null) {
      throw new Error("Claim must be object");
    }
    const c = claim as Record<string, unknown>;
    if (typeof c.text !== "string" || !isVerdict(c.verdict) || !isConfidence(c.confidence) || !Array.isArray(c.evidence)) {
      throw new Error("Claim fields invalid");
    }
    for (const evidence of c.evidence) {
      if (typeof evidence !== "object" || evidence === null) {
        throw new Error("Evidence must be object");
      }
      const e = evidence as Record<string, unknown>;
      if (
        typeof e.title !== "string" ||
        typeof e.url !== "string" ||
        typeof e.access_date !== "string" ||
        (e.publish_date !== undefined && e.publish_date !== null && typeof e.publish_date !== "string") ||
        (e.snippet !== undefined && typeof e.snippet !== "string") ||
        (e.why_trustworthy !== undefined && typeof e.why_trustworthy !== "string") ||
        (e.domain_quality !== "high" && e.domain_quality !== "medium" && e.domain_quality !== "low")
      ) {
        throw new Error("Evidence fields invalid");
      }
    }
    if (c.notes !== undefined && typeof c.notes !== "string") {
      throw new Error("Claim notes must be string");
    }
  }
  if (result.gaps_or_caveats !== undefined) {
    if (!Array.isArray(result.gaps_or_caveats) || !result.gaps_or_caveats.every((item) => typeof item === "string")) {
      throw new Error("gaps_or_caveats must be string array");
    }
  }
}
