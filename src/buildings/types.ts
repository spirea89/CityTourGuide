export type SourceQuality = "high" | "medium" | "low";

export type Evidence = {
  title: string;
  url: string;
  publish_date?: string | null;
  access_date: string;
  snippet?: string;
  source_quality: SourceQuality;
  why_trustworthy: string;
};

export type BuildingFact =
  | { key: "name"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "address"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "coordinates"; value: { lat: number; lon: number }; evidence: Evidence[]; confidence: number }
  | { key: "osm_id"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "wikidata_qid"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "wikipedia_title"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "construction_start"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "construction_end"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "architect"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "architectural_style"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "heritage_designation"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "levels"; value: number; evidence: Evidence[]; confidence: number }
  | { key: "height_m"; value: number; evidence: Evidence[]; confidence: number }
  | { key: "current_use"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "historic_use"; value: string; evidence: Evidence[]; confidence: number }
  | { key: "notable_event"; value: string; evidence: Evidence[]; confidence: number };

export type BuildingFactsResult = {
  query: { address?: string; lat?: number; lon?: number; locale?: string };
  canonical: {
    address?: string;
    lat?: number;
    lon?: number;
    osm_id?: string;
    osm_type?: "node" | "way" | "relation";
    wikidata_qid?: string;
    wikipedia_title?: string;
  };
  summary?: string;
  facts: BuildingFact[];
  verdict: "true" | "mixed" | "uncertain";
  confidence: number;
  notes?: string[];
};
