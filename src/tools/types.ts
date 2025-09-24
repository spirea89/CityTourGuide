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
