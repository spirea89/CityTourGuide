export type WikidataFacts = {
  qid?: string;
  label?: string;
  inception?: string;
  architect?: string;
  style?: string;
  heritage?: string;
  coords?: { lat: number; lon: number };
  wikipediaTitle?: string;
};

export class WikidataProvider {
  constructor(private opts: { userAgent?: string } = {}) {}

  async resolveFromWikipedia(title: string, lang: string): Promise<string | null> {
    const cleanTitle = title.replace(/_/g, " ").trim();
    if (!cleanTitle) {
      return null;
    }
    const safeLang = lang ? lang.toLowerCase() : "en";
    const query = `
PREFIX schema: <http://schema.org/>
SELECT ?item WHERE {
  ?article schema:about ?item ;
           schema:isPartOf <https://${safeLang}.wikipedia.org/> ;
           schema:name ?name .
  FILTER (lcase(str(?name)) = lcase("${escapeSparqlLiteral(cleanTitle)}"))
}
LIMIT 1
`;
    const result = await this.execute(query);
    if (!result) {
      return null;
    }
    const binding = result.results?.bindings?.[0];
    const item = binding?.item?.value;
    if (!item || typeof item !== "string") {
      return null;
    }
    const qid = item.substring(item.lastIndexOf("/") + 1);
    return qid || null;
  }

  async fetchFactsByQid(qid: string, lang: string): Promise<WikidataFacts | null> {
    if (!qid) {
      return null;
    }
    const safeLang = lang ? lang.toLowerCase() : "en";
    const query = `
PREFIX wikibase: <http://wikiba.se/ontology#>
PREFIX wd: <http://www.wikidata.org/entity/>
PREFIX wdt: <http://www.wikidata.org/prop/direct/>
PREFIX schema: <http://schema.org/>
SELECT ?itemLabel ?inceptionValue ?architectLabel ?styleLabel ?heritageLabel ?coord ?articleTitle WHERE {
  BIND(wd:${qid} AS ?item)
  OPTIONAL { ?item wdt:P571 ?inceptionRaw . BIND(STR(?inceptionRaw) AS ?inceptionValue) }
  OPTIONAL { ?item wdt:P84 ?architect }
  OPTIONAL { ?item wdt:P149 ?style }
  OPTIONAL { ?item wdt:P1435 ?heritage }
  OPTIONAL { ?item wdt:P625 ?coord }
  OPTIONAL {
    ?article schema:about ?item ;
             schema:isPartOf <https://${safeLang}.wikipedia.org/> ;
             schema:name ?articleTitle .
  }
  SERVICE wikibase:label { bd:serviceParam wikibase:language "${safeLang},en". }
}
LIMIT 1
`;
    const result = await this.execute(query);
    if (!result) {
      return null;
    }
    const binding = result.results?.bindings?.[0];
    if (!binding) {
      return { qid };
    }
    const facts: WikidataFacts = { qid };
    if (binding.itemLabel?.value) {
      facts.label = binding.itemLabel.value;
    }
    if (binding.inceptionValue?.value) {
      facts.inception = binding.inceptionValue.value;
    }
    if (binding.architectLabel?.value) {
      facts.architect = binding.architectLabel.value;
    }
    if (binding.styleLabel?.value) {
      facts.style = binding.styleLabel.value;
    }
    if (binding.heritageLabel?.value) {
      facts.heritage = binding.heritageLabel.value;
    }
    if (binding.coord?.value && typeof binding.coord.value === "string") {
      const match = binding.coord.value.match(/Point\(([-0-9.]+) ([-0-9.]+)\)/);
      if (match) {
        const lon = Number.parseFloat(match[1]);
        const lat = Number.parseFloat(match[2]);
        if (Number.isFinite(lat) && Number.isFinite(lon)) {
          facts.coords = { lat, lon };
        }
      }
    }
    if (binding.articleTitle?.value) {
      facts.wikipediaTitle = binding.articleTitle.value;
    }
    return facts;
  }

  private async execute(query: string): Promise<any | null> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 12000);
    try {
      const body = new URLSearchParams({ query }).toString();
      const response = await fetch("https://query.wikidata.org/sparql", {
        method: "POST",
        headers: {
          "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
          Accept: "application/sparql-results+json",
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
      return JSON.parse(text);
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }
}

function escapeSparqlLiteral(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}
