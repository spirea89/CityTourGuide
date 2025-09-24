import test from 'node:test';
import assert from 'node:assert/strict';
import { verifyParagraph } from '../src/factcheck/verify';
import type { WebSearchTool } from '../src/tools/types';

class MemoryMock implements WebSearchTool {
  constructor(
    private results: Record<string, Array<{ title: string; url: string; snippet?: string; publishDateIso?: string }>>,
    private openBodies: Record<string, string> = {}
  ) {}

  async searchWeb(q: string, _opts?: { recencyDays?: number; max?: number }) {
    return this.results[q] ?? [];
  }

  async openUrl(url: string) {
    return { ok: true, finalUrl: url, text: this.openBodies[url] ?? '' };
  }
}

test('returns uncertain when evidence is thin', async () => {
  const paragraph = 'Ungargasse 5 was completed in 1871 and served as the Rothschild Hospital.';
  const tools = new MemoryMock({});
  const result = await verifyParagraph({ paragraph, tools });

  assert.equal(result.verdict, 'uncertain');
  assert.ok(result.claims.length >= 1);
  for (const claim of result.claims) {
    assert.equal(claim.verdict, 'uncertain');
    assert.ok(claim.confidence <= 0.5);
    for (const evidence of claim.evidence) {
      assert.equal(typeof evidence.access_date, 'string');
    }
  }
});

test('handles mixed evidence and assigns per-claim verdicts', async () => {
  const paragraph = 'Ungargasse 5 is in Vienna\'s 3rd district and Beethoven lived there.';
  const tools = new MemoryMock({
    'Ungargasse 5 district Vienna': [
      {
        title: 'Wien Landstraße Address Register',
        url: 'https://www.wien.gv.at/landstrasse/addresses/ungargasse-5',
        snippet: 'Ungargasse 5, 1030 Wien (Landstraße)',
      },
    ],
    'Beethoven Ungargasse 5 plaque': [
      {
        title: 'Beethoven in Ungargasse 5',
        url: 'https://example-museum.at/beethoven-ungargasse-5',
        snippet: 'A plaque indicates Beethoven stayed in 1823',
      },
    ],
  });

  const result = await verifyParagraph({ paragraph, tools });

  assert.ok(result.verdict === 'true' || result.verdict === 'mixed');
  assert.ok(result.claims.length >= 2);

  const districtClaim = result.claims.find((claim) => /3rd district/i.test(claim.text));
  assert.ok(districtClaim, 'Expected a claim about the 3rd district');
  assert.equal(districtClaim.verdict, 'true');

  const beethovenClaim = result.claims.find((claim) => /Beethoven/i.test(claim.text));
  assert.ok(beethovenClaim, 'Expected a claim about Beethoven');
  assert.ok(beethovenClaim.verdict === 'true' || beethovenClaim.verdict === 'mixed');
  assert.ok(beethovenClaim.confidence > 0.4);

  for (const claim of result.claims) {
    for (const evidence of claim.evidence) {
      assert.equal(typeof evidence.access_date, 'string');
    }
  }
});
