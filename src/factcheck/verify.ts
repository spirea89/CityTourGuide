export interface WebSearchTool {
  searchWeb(
    query: string,
    opts?: { recencyDays?: number; max?: number }
  ): Promise<
    Array<{
      title: string;
      url: string;
      snippet?: string;
      publishDateIso?: string | null;
    }>
  >;
  openUrl(url: string): Promise<{ ok: boolean; finalUrl: string; text?: string }>;
}

type ClaimVerdict = "true" | "false" | "mixed" | "uncertain";
export type DomainQuality = "high" | "medium" | "low";

export type FactCheckResult = {
  question: string;
  verdict: ClaimVerdict;
  confidence: number;
  claims: Array<{
    text: string;
    verdict: ClaimVerdict;
    confidence: number;
    evidence: Array<{
      title: string;
      url: string;
      publish_date?: string | null;
      access_date: string;
      snippet?: string;
      domain_quality: DomainQuality;
      why_trustworthy: string;
    }>;
    notes?: string;
  }>;
  gaps_or_caveats?: string[];
};

type Evidence = FactCheckResult["claims"][number]["evidence"][number];

type SearchResultItem = {
  title: string;
  url: string;
  snippet?: string;
  publishDateIso?: string | null;
};

type EvaluatedEvidence = {
  evidence: Evidence;
  stance: "support" | "refute" | "uncertain";
  qualityScore: number;
  matchScore: number;
  domain: string;
};

type VerifyParagraphInput = {
  paragraph: string;
  nowIso?: string;
  minSourcesPerClaim?: number;
  tools: WebSearchTool;
};

const STOP_WORDS = new Set(
  [
    "the",
    "a",
    "an",
    "and",
    "or",
    "but",
    "if",
    "then",
    "because",
    "while",
    "with",
    "into",
    "onto",
    "from",
    "that",
    "this",
    "these",
    "those",
    "for",
    "was",
    "were",
    "is",
    "are",
    "been",
    "being",
    "of",
    "on",
    "in",
    "at",
    "by",
    "to",
    "as",
    "it",
    "its",
    "their",
    "his",
    "her",
    "they",
    "them",
    "he",
    "she",
    "we",
    "you",
    "i",
    "about",
    "over",
    "under",
    "after",
    "before",
    "near",
    "more",
    "less",
    "up",
    "down",
    "through",
    "during",
    "per",
    "each",
    "every",
    "any",
    "some",
    "many",
    "most",
    "few",
    "several",
    "various",
    "across",
    "such",
    "so",
    "than",
    "within",
    "without",
    "including",
    "between",
    "over",
    "new",
    "old",
    "city",
    "town",
    "village",
    "district",
    "county",
    "state",
    "country",
    "province",
    "region",
    "year",
    "years",
    "month",
    "months",
    "day",
    "days",
    "season",
    "seasons",
    "century",
    "centuries",
    "built",
    "located",
    "located",
    "located",
    "historic",
    "historic",
    "official",
    "officially",
    "opened",
    "opening",
    "founded",
    "founded"
  ].map((w) => w.toLowerCase())
);

function normalizeWhitespace(value: string): string {
  return value.replace(/\s+/g, " ").trim();
}

function stripTrailingPunctuation(value: string): string {
  return value.replace(/[.,;:!?\s]+$/u, "").trim();
}

function splitSentenceIntoClaims(sentence: string): string[] {
  const segments = sentence
    .split(/(?:,|;|\band\b|\bwhere\b|\bwhich\b)/iu)
    .map((part) => normalizeWhitespace(part))
    .filter((part) => part.length > 0);
  return segments.length > 0 ? segments : [normalizeWhitespace(sentence)];
}

function dedupeClaims(claims: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const claim of claims) {
    const key = normalizeWhitespace(claim).toLocaleLowerCase("en");
    if (!seen.has(key)) {
      seen.add(key);
      result.push(normalizeWhitespace(claim));
    }
  }
  return result;
}

export function extractClaims(paragraph: string): string[] {
  const sanitized = normalizeWhitespace(paragraph);
  if (!sanitized) {
    return [];
  }
  let sentences = sanitized
    .split(/(?<=[.!?])\s+/u)
    .map((part) => normalizeWhitespace(stripTrailingPunctuation(part)))
    .filter((part) => part.length > 0);
  if (sentences.length === 0) {
    sentences = [sanitized];
  }
  let claims: string[] = [];
  for (const sentence of sentences) {
    const segments = splitSentenceIntoClaims(sentence);
    for (const segment of segments) {
      if (segment.length > 0) {
        claims.push(segment);
      }
    }
  }
  let normalizedClaims = claims
    .map((claim) => {
      const lower = claim.toLowerCase();
      let adjusted = claim;
      if (lower.includes("vienna")) {
        adjusted = adjusted.replace(/Vienna/giu, "Vienna (Wien)");
      }
      return normalizeWhitespace(stripTrailingPunctuation(adjusted));
    })
    .filter((claim) => claim.length > 0);
  normalizedClaims = dedupeClaims(normalizedClaims);
  while (normalizedClaims.length < 4) {
    const longest = normalizedClaims.reduce(
      (prev, current) => (current.length > prev.length ? current : prev),
      ""
    );
    if (!longest || longest.length < 20) {
      break;
    }
    const index = normalizedClaims.indexOf(longest);
    if (index >= 0) {
      normalizedClaims.splice(index, 1);
    }
    const splitPieces = splitSentenceIntoClaims(longest)
      .map((piece) => normalizeWhitespace(stripTrailingPunctuation(piece)))
      .filter((piece) => piece.length > 0);
    normalizedClaims = dedupeClaims([...normalizedClaims, ...splitPieces]);
  }
  if (normalizedClaims.length > 12) {
    normalizedClaims = normalizedClaims.slice(0, 12);
  }
  return normalizedClaims;
}

function getKeywords(claim: string, max = 8): string[] {
  const clean = claim
    .replace(/[^\p{L}\p{N}\s-]/gu, " ")
    .split(/\s+/u)
    .map((token) => token.trim().toLowerCase())
    .filter((token) => token.length > 2 && !STOP_WORDS.has(token));
  const unique: string[] = [];
  for (const token of clean) {
    if (!unique.includes(token)) {
      unique.push(token);
    }
  }
  return unique.slice(0, max);
}

function applyLocaleVariants(terms: string[]): string[] {
  const variants = new Set<string>(terms);
  for (const term of terms) {
    if (term.includes("vienna")) {
      variants.add(term.replace(/vienna/giu, "wien"));
    }
    if (term.includes("austria")) {
      variants.add(term.replace(/austria/giu, "österreich"));
    }
  }
  return Array.from(variants);
}

export function buildQueries(claim: string): string[] {
  const normalized = normalizeWhitespace(claim);
  const queries = new Set<string>();
  if (normalized.length > 0) {
    queries.add(normalized);
  }
  const keywords = getKeywords(normalized);
  if (keywords.length > 0) {
    const condensed = keywords.slice(0, 6).join(" ");
    queries.add(condensed);
    if (keywords.length > 2) {
      queries.add(`"${keywords.slice(0, 2).join(" " )}" ${keywords.slice(2, 5).join(" ")}`.trim());
    }
  }
  const variantKeywords = applyLocaleVariants(keywords);
  if (variantKeywords.length > 0) {
    queries.add(variantKeywords.slice(0, 5).join(" "));
  }
  const uniqueQueries = Array.from(queries).filter((q) => q.length > 0);
  if (uniqueQueries.length === 0 && normalized.length > 0) {
    uniqueQueries.push(normalized);
  }
  return uniqueQueries.slice(0, 4);
}

function canonicalizeUrl(url: string): string {
  try {
    const parsed = new URL(url);
    const path = parsed.pathname.replace(/\/+$/, "");
    return `${parsed.origin}${path}${parsed.search}`;
  } catch {
    return url.trim();
  }
}

function hostnameFromUrl(url: string): string {
  try {
    const parsed = new URL(url);
    return parsed.hostname.toLowerCase();
  } catch {
    return url.toLowerCase();
  }
}

function qualityScoreForDomain(domainQuality: DomainQuality): number {
  switch (domainQuality) {
    case "high":
      return 1;
    case "medium":
      return 0.7;
    default:
      return 0.4;
  }
}

function describeDomain(domain: string): { domain_quality: DomainQuality; why: string } {
  const normalized = domain.replace(/^www\./u, "");
  const lower = normalized.toLowerCase();
  const highPatterns = [
    ".gv.at",
    ".gv.",
    ".gov",
    ".gob",
    ".mil",
    ".admin.ch",
    ".parliament.",
    ".bund.",
    ".statistik",
    ".ac.",
    ".edu",
    ".uni",
    ".museum",
    ".int"
  ];
  for (const pattern of highPatterns) {
    if (lower.includes(pattern)) {
      return { domain_quality: "high", why: "Official or academic domain" };
    }
  }
  const reputableNews = [
    "orf.at",
    "derstandard",
    "diepresse",
    "kurier",
    "apa.at",
    "salzburg24",
    "tiroler",
    "wienerzeitung",
    "zeit.de",
    "faz.net",
    "bbc",
    "reuters",
    "apnews",
    "theguardian",
    "nytimes",
    "washingtonpost"
  ];
  if (reputableNews.some((name) => lower.includes(name))) {
    return { domain_quality: "high", why: "Established news outlet" };
  }
  if (lower.endsWith(".at") || lower.endsWith(".de") || lower.endsWith(".eu")) {
    return { domain_quality: "medium", why: "Regional information source" };
  }
  if (lower.includes("blog") || lower.includes("forum")) {
    return { domain_quality: "low", why: "User-generated content" };
  }
  return { domain_quality: "medium", why: "General web source" };
}

export function scoreSource(url: string, title?: string): { domain_quality: DomainQuality; why: string } {
  const domain = hostnameFromUrl(url);
  const base = describeDomain(domain);
  if (base.domain_quality === "medium" && title) {
    const lowerTitle = title.toLowerCase();
    if (
      /official|stadt|museum|universit|archive|amt|ministerium|city hall|government|verwaltung/u.test(
        lowerTitle
      )
    ) {
      return { domain_quality: "high", why: "Officially described content" };
    }
  }
  return base;
}

function trimSnippet(snippet?: string): string | undefined {
  if (!snippet) {
    return undefined;
  }
  const clean = normalizeWhitespace(snippet);
  const words = clean.split(/\s+/u);
  if (words.length <= 25) {
    return clean;
  }
  return `${words.slice(0, 25).join(" ")}…`;
}

function computeMatchScore(text: string, keywords: string[]): number {
  if (!text) {
    return 0;
  }
  if (keywords.length === 0) {
    return 0;
  }
  const normalized = text.toLowerCase();
  let hits = 0;
  for (const keyword of keywords) {
    if (normalized.includes(keyword.toLowerCase())) {
      hits += 1;
    }
  }
  return hits / keywords.length;
}

function detectNegation(text: string, keywords: string[]): boolean {
  const normalized = text.toLowerCase();
  const negativeTerms = ["not", "no", "false", "fake", "denies", "denied", "never", "myth", "hoax"];
  for (const keyword of keywords) {
    const lowered = keyword.toLowerCase();
    let index = normalized.indexOf(lowered);
    while (index !== -1) {
      const window = normalized.slice(Math.max(0, index - 20), index + lowered.length + 20);
      if (negativeTerms.some((term) => window.includes(term))) {
        return true;
      }
      index = normalized.indexOf(lowered, index + lowered.length);
    }
  }
  return false;
}

async function fetchTextForUrl(
  url: string,
  tools?: WebSearchTool
): Promise<{ finalUrl: string; text?: string }> {
  if (!tools) {
    return { finalUrl: url };
  }
  try {
    const result = await tools.openUrl(url);
    if (!result.ok) {
      return { finalUrl: result.finalUrl || url };
    }
    return { finalUrl: result.finalUrl || url, text: result.text };
  } catch {
    return { finalUrl: url };
  }
}

function determineStance(text: string | undefined, keywords: string[]): "support" | "refute" | "uncertain" {
  if (!text) {
    return "uncertain";
  }
  const score = computeMatchScore(text, keywords);
  if (score >= 0.6) {
    return detectNegation(text, keywords) ? "refute" : "support";
  }
  if (score >= 0.3) {
    return detectNegation(text, keywords) ? "refute" : "support";
  }
  return "uncertain";
}

export async function pickEvidence(
  claim: string,
  results: SearchResultItem[],
  options?: { tools?: WebSearchTool; accessDate?: string; minSources?: number; maxSources?: number }
): Promise<EvaluatedEvidence[]> {
  const accessDate = options?.accessDate ?? getCurrentViennaIso();
  const minSources = options?.minSources ?? 2;
  const maxSources = Math.max(options?.maxSources ?? Math.max(minSources, 4), minSources);
  const keywords = getKeywords(claim);
  const aggregated = new Map<string, SearchResultItem>();
  for (const result of results) {
    const key = canonicalizeUrl(result.url);
    if (!aggregated.has(key)) {
      aggregated.set(key, result);
    } else {
      const existing = aggregated.get(key)!;
      if (!existing.snippet && result.snippet) {
        aggregated.set(key, { ...existing, snippet: result.snippet });
      }
      if (!existing.publishDateIso && result.publishDateIso) {
        aggregated.set(key, { ...existing, publishDateIso: result.publishDateIso });
      }
    }
  }
  const evaluated: EvaluatedEvidence[] = [];
  const candidates: Array<{ item: SearchResultItem; domain: string; score: number; quality: DomainQuality }> = [];
  for (const item of aggregated.values()) {
    const domain = hostnameFromUrl(item.url);
    const { domain_quality } = scoreSource(item.url, item.title);
    const matchScore = computeMatchScore(item.snippet ?? "", keywords);
    candidates.push({ item, domain, score: matchScore, quality: domain_quality });
  }
  candidates.sort((a, b) => {
    const qualityWeight = qualityScoreForDomain(b.quality) - qualityScoreForDomain(a.quality);
    if (qualityWeight !== 0) {
      return qualityWeight;
    }
    return b.score - a.score;
  });
  const usedDomains = new Set<string>();
  for (const candidate of candidates) {
    if (evaluated.length >= maxSources) {
      break;
    }
    if (usedDomains.has(candidate.domain) && evaluated.length < minSources) {
      continue;
    }
    const snippet = trimSnippet(candidate.item.snippet);
    let textForAnalysis = snippet;
    let finalUrl = candidate.item.url;
    if ((!textForAnalysis || computeMatchScore(textForAnalysis, keywords) < 0.4) && options?.tools) {
      const fetched = await fetchTextForUrl(candidate.item.url, options.tools);
      finalUrl = fetched.finalUrl || candidate.item.url;
      textForAnalysis = fetched.text ? normalizeWhitespace(fetched.text).slice(0, 2000) : textForAnalysis;
    }
    const { domain_quality, why } = scoreSource(finalUrl, candidate.item.title);
    const stance = determineStance(textForAnalysis, keywords);
    const evidence: Evidence = {
      title: candidate.item.title,
      url: finalUrl,
      publish_date: candidate.item.publishDateIso ?? undefined,
      access_date: accessDate,
      snippet: snippet,
      domain_quality,
      why_trustworthy: why
    };
    evaluated.push({
      evidence,
      stance,
      qualityScore: qualityScoreForDomain(domain_quality),
      matchScore: computeMatchScore(textForAnalysis ?? "", keywords),
      domain: hostnameFromUrl(finalUrl)
    });
    usedDomains.add(candidate.domain);
    usedDomains.add(hostnameFromUrl(finalUrl));
  }
  if (evaluated.length < minSources) {
    for (const candidate of candidates) {
      if (evaluated.length >= Math.min(maxSources, candidates.length)) {
        break;
      }
      if (evaluated.some((item) => canonicalizeUrl(item.evidence.url) === canonicalizeUrl(candidate.item.url))) {
        continue;
      }
      const snippet = trimSnippet(candidate.item.snippet);
      const { domain_quality, why } = scoreSource(candidate.item.url, candidate.item.title);
      const stance = determineStance(snippet, keywords);
      const evidence: Evidence = {
        title: candidate.item.title,
        url: candidate.item.url,
        publish_date: candidate.item.publishDateIso ?? undefined,
        access_date: accessDate,
        snippet,
        domain_quality,
        why_trustworthy: why
      };
      evaluated.push({
        evidence,
        stance,
        qualityScore: qualityScoreForDomain(domain_quality),
        matchScore: computeMatchScore(snippet ?? "", keywords),
        domain: hostnameFromUrl(candidate.item.url)
      });
      if (evaluated.length >= minSources) {
        break;
      }
    }
  }
  return evaluated.slice(0, maxSources);
}

export function decideVerdict(
  evidences: EvaluatedEvidence[],
  minSources: number
): { verdict: ClaimVerdict; confidence: number; notes?: string } {
  const support = evidences.filter((item) => item.stance === "support");
  const refute = evidences.filter((item) => item.stance === "refute");
  const supportScore = support.reduce((sum, item) => sum + item.qualityScore, 0);
  const refuteScore = refute.reduce((sum, item) => sum + item.qualityScore, 0);
  const totalEvidence = evidences.filter((item) => item.stance !== "uncertain");
  let verdict: ClaimVerdict = "uncertain";
  let notes: string | undefined;
  if (support.length >= minSources && refute.length === 0) {
    verdict = "true";
  } else if (refute.length >= minSources && support.length === 0) {
    verdict = "false";
  } else if (support.length > 0 && refute.length > 0) {
    verdict = "mixed";
    notes = "Conflicting reputable sources";
  } else if (support.length === 0 && refute.length === 0) {
    verdict = "uncertain";
  } else if (supportScore > refuteScore && support.length >= minSources) {
    verdict = "true";
  } else if (refuteScore > supportScore && refute.length >= minSources) {
    verdict = "false";
  } else {
    verdict = "uncertain";
  }
  if (verdict === "true" && support.length < minSources) {
    verdict = "uncertain";
    notes = "Not enough independent confirmation";
  }
  if (verdict === "false" && refute.length < minSources) {
    verdict = "uncertain";
    notes = "Not enough independent contradiction";
  }
  let confidence = 0;
  if (totalEvidence.length > 0) {
    confidence = Math.min(0.9, 0.25 + totalEvidence.length * 0.15);
    const avgQuality =
      totalEvidence.reduce((sum, item) => sum + item.qualityScore, 0) /
      totalEvidence.length;
    confidence *= 0.6 + 0.4 * avgQuality;
  } else {
    confidence = 0.1;
  }
  if (verdict === "uncertain") {
    confidence = Math.min(confidence, 0.35);
  }
  if (verdict === "mixed") {
    confidence = Math.min(confidence, 0.55);
  }
  confidence = Math.max(0, Math.min(1, confidence));
  return { verdict, confidence, notes };
}

export function aggregateVerdict(
  claims: Array<{ verdict: ClaimVerdict; confidence: number }>
): { verdict: ClaimVerdict; confidence: number } {
  if (claims.length === 0) {
    return { verdict: "uncertain", confidence: 0 };
  }
  const totalConfidence = claims.reduce((sum, claim) => sum + claim.confidence, 0);
  if (totalConfidence === 0) {
    return { verdict: "uncertain", confidence: 0 };
  }
  const verdictWeights: Record<ClaimVerdict, number> = {
    true: 1,
    false: -1,
    mixed: 0,
    uncertain: 0
  };
  const score =
    claims.reduce((sum, claim) => sum + claim.confidence * verdictWeights[claim.verdict], 0) /
    totalConfidence;
  const hasMixed = claims.some((claim) => claim.verdict === "mixed");
  let verdict: ClaimVerdict;
  if (score > 0.5) {
    verdict = "true";
  } else if (score < -0.5) {
    verdict = "false";
  } else if (hasMixed) {
    verdict = "mixed";
  } else {
    verdict = "uncertain";
  }
  const averageConfidence = totalConfidence / claims.length;
  const finalConfidence = Math.min(0.95, Math.max(0.05, Math.abs(score) * 0.5 + averageConfidence * 0.7));
  return { verdict, confidence: finalConfidence };
}

function getCurrentViennaIso(): string {
  const now = new Date();
  const formatter = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/Vienna",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23"
  });
  const parts = formatter.formatToParts(now);
  const part = Object.fromEntries(parts.map((entry) => [entry.type, entry.value]));
  const date = `${part.year}-${part.month}-${part.day}`;
  const time = `${part.hour}:${part.minute}:${part.second}`;
  const offsetFormatter = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/Vienna",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
    timeZoneName: "shortOffset"
  });
  const offsetPart = offsetFormatter
    .formatToParts(now)
    .find((entry) => entry.type === "timeZoneName")?.value;
  let offset = offsetPart ? offsetPart.replace("GMT", "").replace("UTC", "") : "+00:00";
  if (!offset || offset === "") {
    offset = "+00:00";
  }
  return `${date}T${time}${offset}`;
}

function assertFactCheckResult(result: FactCheckResult): asserts result is FactCheckResult {
  if (!result || typeof result !== "object") {
    throw new Error("Invalid result");
  }
  if (typeof result.question !== "string") {
    throw new Error("Missing question");
  }
  const validVerdicts: ClaimVerdict[] = ["true", "false", "mixed", "uncertain"];
  const validDomainQualities: DomainQuality[] = ["high", "medium", "low"];
  if (!validVerdicts.includes(result.verdict)) {
    throw new Error("Invalid verdict");
  }
  if (
    typeof result.confidence !== "number" ||
    Number.isNaN(result.confidence) ||
    result.confidence < 0 ||
    result.confidence > 1
  ) {
    throw new Error("Invalid confidence");
  }
  if (!Array.isArray(result.claims)) {
    throw new Error("Claims must be an array");
  }
  for (const claim of result.claims) {
    if (typeof claim.text !== "string" || claim.text.length === 0) {
      throw new Error("Invalid claim text");
    }
    if (!validVerdicts.includes(claim.verdict)) {
      throw new Error("Invalid claim verdict");
    }
    if (
      typeof claim.confidence !== "number" ||
      Number.isNaN(claim.confidence) ||
      claim.confidence < 0 ||
      claim.confidence > 1
    ) {
      throw new Error("Invalid claim confidence");
    }
    if (!Array.isArray(claim.evidence)) {
      throw new Error("Invalid evidence");
    }
    for (const evidence of claim.evidence) {
      if (typeof evidence.title !== "string" || typeof evidence.url !== "string") {
        throw new Error("Evidence requires title and url");
      }
      if (typeof evidence.access_date !== "string") {
        throw new Error("Evidence missing access_date");
      }
      if (!validDomainQualities.includes(evidence.domain_quality)) {
        throw new Error("Evidence missing domain quality");
      }
      if (typeof evidence.why_trustworthy !== "string") {
        throw new Error("Evidence missing why_trustworthy");
      }
    }
  }
  if (result.gaps_or_caveats) {
    if (!Array.isArray(result.gaps_or_caveats)) {
      throw new Error("gaps_or_caveats must be array");
    }
  }
}

export async function verifyParagraph(input: VerifyParagraphInput): Promise<FactCheckResult> {
  const { paragraph, tools } = input;
  const minSources = Math.max(1, input.minSourcesPerClaim ?? 2);
  const nowIso = input.nowIso ?? getCurrentViennaIso();
  const claimsTexts = extractClaims(paragraph);
  if (claimsTexts.length === 0) {
    const result: FactCheckResult = {
      question: "Is paragraph true?",
      verdict: "uncertain",
      confidence: 0.1,
      claims: [],
      gaps_or_caveats: ["No verifiable claims found"]
    };
    assertFactCheckResult(result);
    return result;
  }
  const claimResults: FactCheckResult["claims"] = [];
  const caveats: string[] = [];
  for (const claimText of claimsTexts) {
    const queries = buildQueries(claimText);
    const aggregatedResults: SearchResultItem[] = [];
    const seenUrls = new Set<string>();
    for (const query of queries) {
      let searchResults: SearchResultItem[] = [];
      try {
        const response = await tools.searchWeb(query, { max: 8 });
        searchResults = response ?? [];
      } catch {
        searchResults = [];
      }
      for (const result of searchResults) {
        const key = canonicalizeUrl(result.url);
        if (seenUrls.has(key)) {
          continue;
        }
        seenUrls.add(key);
        aggregatedResults.push(result);
      }
    }
    const evaluated = await pickEvidence(claimText, aggregatedResults, {
      tools,
      accessDate: nowIso,
      minSources,
      maxSources: Math.max(minSources, 4)
    });
    const decision = decideVerdict(evaluated, minSources);
    const evidence = evaluated.map((item) => item.evidence);
    const claimResult: FactCheckResult["claims"][number] = {
      text: claimText,
      verdict: decision.verdict,
      confidence: decision.confidence,
      evidence
    };
    if (decision.notes) {
      claimResult.notes = decision.notes;
    }
    if (decision.verdict === "uncertain" && aggregatedResults.length === 0) {
      caveats.push(`No reliable sources found for claim: ${claimText}`);
    } else if (decision.verdict === "uncertain" && decision.notes) {
      caveats.push(`${decision.notes} for claim: ${claimText}`);
    }
    claimResults.push(claimResult);
  }
  const overall = aggregateVerdict(claimResults.map(({ verdict, confidence }) => ({ verdict, confidence })));
  const factCheckResult: FactCheckResult = {
    question: "Is paragraph true?",
    verdict: overall.verdict,
    confidence: overall.confidence,
    claims: claimResults,
    gaps_or_caveats: caveats.length > 0 ? caveats : undefined
  };
  assertFactCheckResult(factCheckResult);
  return factCheckResult;
}
