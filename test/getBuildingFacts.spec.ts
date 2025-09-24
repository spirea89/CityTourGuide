import test from "node:test";
import assert from "node:assert/strict";
import { getBuildingFacts, __testOnly } from "../src/buildings/getBuildingFacts.js";
import type { BuildingFactsResult } from "../src/buildings/types.js";

test("uncertain when sources are thin", async () => {
  __testOnly.setProviders({
    geocoder: {
      async geocodeAddress() {
        return null;
      },
    },
    overpass: {
      async findNearestBuilding() {
        return {
          id: "1",
          type: "way",
          tags: {
            name: "Testhaus",
            start_date: "1872",
          },
        };
      },
    },
    wikidata: {
      async resolveFromWikipedia() {
        return null;
      },
      async fetchFactsByQid() {
        return null;
      },
    },
  });

  const result: BuildingFactsResult = await getBuildingFacts({ lat: 48.2, lon: 16.38, nowIso: "2024-03-01" });

  assert.equal(result.verdict, "uncertain");
  const startFact = result.facts.find((fact) => fact.key === "construction_start");
  assert.ok(startFact, "missing construction_start fact");
  assert.ok(startFact.confidence <= 0.4, "confidence too high for single medium source");
  for (const fact of result.facts) {
    for (const evidence of fact.evidence) {
      assert.ok(evidence.access_date, "evidence missing access_date");
    }
  }

  __testOnly.setProviders({ geocoder: undefined, overpass: undefined, wikidata: undefined });
});

test("corroborates across Overpass + Wikidata", async () => {
  __testOnly.setProviders({
    geocoder: {
      async geocodeAddress() {
        return null;
      },
    },
    overpass: {
      async findNearestBuilding() {
        return {
          id: "2",
          type: "way",
          lat: 48.201,
          lon: 16.382,
          tags: {
            name: "Haus Beispiel",
            building: "residential",
            "building:levels": "5",
            start_date: "1801",
            wikidata: "Q123456",
          },
        };
      },
    },
    wikidata: {
      async resolveFromWikipedia() {
        return null;
      },
      async fetchFactsByQid(qid: string) {
        return {
          qid,
          label: "Haus Beispiel",
          inception: "1801-01-01",
          architect: "Max Mustermann",
          style: "Biedermeier",
          heritage: "Listed building",
          coords: { lat: 48.201, lon: 16.382 },
          wikipediaTitle: "Haus Beispiel",
        };
      },
    },
  });

  const result: BuildingFactsResult = await getBuildingFacts({ lat: 48.201, lon: 16.382, nowIso: "2024-03-01" });

  assert.notEqual(result.verdict, "uncertain");
  const factKeys = result.facts.map((fact) => fact.key);
  assert.ok(factKeys.includes("name"), "name fact missing");
  assert.ok(factKeys.includes("coordinates"), "coordinates fact missing");
  assert.ok(factKeys.includes("wikidata_qid"), "wikidata fact missing");
  assert.ok(
    factKeys.includes("construction_start") ||
      factKeys.includes("architect") ||
      factKeys.includes("architectural_style"),
    "expected construction or architect/style fact"
  );

  for (const fact of result.facts) {
    const evidenceCount = fact.evidence.length;
    if (evidenceCount === 1) {
      assert.ok(
        fact.confidence <= 0.6,
        `${fact.key} has confidence ${fact.confidence} but only one source`
      );
    } else if (evidenceCount >= 2) {
      assert.ok(
        fact.confidence > 0.6,
        `${fact.key} should exceed 0.6 confidence with multiple sources`
      );
    }
  }

  assert.ok(result.summary && result.summary.split(/\s+/).filter(Boolean).length <= 120, "summary exceeds 120 words");

  __testOnly.setProviders({ geocoder: undefined, overpass: undefined, wikidata: undefined });
});
