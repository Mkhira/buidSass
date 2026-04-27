/**
 * T009 — CSV header + row parsing for the bulk-import wizard.
 *
 * The schema version travels in the export response's
 * `X-Bulk-Import-Schema-Version` header; the UI compares headers
 * against the schema before submitting. Parsing uses `papaparse` so
 * quoting / locale-aware decimal separators are handled.
 */
import Papa from "papaparse";

export interface CsvParseError {
  rowIndex: number;
  field: string | null;
  message: string;
}

export interface CsvParseResult {
  headers: string[];
  rows: Array<Record<string, string>>;
  errors: CsvParseError[];
}

export function parseHeader(input: string): string[] {
  const firstLine = input.split(/\r?\n/, 1)[0] ?? "";
  const result = Papa.parse<string[]>(firstLine, { skipEmptyLines: true });
  return (result.data[0] ?? []).map((h) => h.trim());
}

export function parseRows(input: string): CsvParseResult {
  const result = Papa.parse<Record<string, string>>(input, {
    header: true,
    skipEmptyLines: true,
    dynamicTyping: false,
    transformHeader: (h) => h.trim(),
  });
  const errors: CsvParseError[] = (result.errors ?? []).map((e) => ({
    rowIndex: e.row ?? -1,
    field: null,
    message: e.message,
  }));
  return {
    headers: result.meta.fields ?? [],
    rows: result.data ?? [],
    errors,
  };
}

export interface ValidationReportRow {
  rowIndex: number;
  field: string | null;
  errorCode: string;
  errorMessage: string;
}

/**
 * Serialize a validation report back to CSV for download. Used by the
 * review step's "Download report" action.
 */
export function serializeReport(rows: ValidationReportRow[]): string {
  return Papa.unparse(rows, {
    columns: ["rowIndex", "field", "errorCode", "errorMessage"],
  });
}

/**
 * Compare uploaded headers against an expected schema. Returns a
 * mismatch reason or `null` if the shapes match.
 */
export function detectSchemaMismatch(
  uploaded: string[],
  expected: string[],
): { kind: "missing" | "extra"; columns: string[] } | null {
  const missing = expected.filter((c) => !uploaded.includes(c));
  if (missing.length > 0) return { kind: "missing", columns: missing };
  const extra = uploaded.filter((c) => !expected.includes(c));
  if (extra.length > 0) return { kind: "extra", columns: extra };
  return null;
}
