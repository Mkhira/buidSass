import { describe, expect, it } from "vitest";
import {
  detectSchemaMismatch,
  parseHeader,
  parseRows,
  serializeReport,
} from "@/lib/catalog/csv";

describe("CSV utils", () => {
  it("parses headers from the first line, trimmed", () => {
    expect(parseHeader("sku, name ,state\n1,Foo,draft")).toEqual([
      "sku",
      "name",
      "state",
    ]);
  });

  it("parses rows into key/value records", () => {
    const out = parseRows("sku,name\nA,Foo\nB,Bar");
    expect(out.headers).toEqual(["sku", "name"]);
    expect(out.rows).toEqual([
      { sku: "A", name: "Foo" },
      { sku: "B", name: "Bar" },
    ]);
    expect(out.errors).toEqual([]);
  });

  it("detects missing schema columns", () => {
    expect(
      detectSchemaMismatch(["sku", "name"], ["sku", "name", "brand"]),
    ).toEqual({ kind: "missing", columns: ["brand"] });
  });

  it("detects extra schema columns", () => {
    expect(detectSchemaMismatch(["sku", "name", "color"], ["sku", "name"]))
      .toEqual({ kind: "extra", columns: ["color"] });
  });

  it("returns null when shapes match", () => {
    expect(detectSchemaMismatch(["a", "b"], ["a", "b"])).toBeNull();
  });

  it("serializeReport round-trips", () => {
    const csv = serializeReport([
      { rowIndex: 1, field: "sku", errorCode: "E1", errorMessage: "bad" },
    ]);
    expect(csv).toContain("rowIndex");
    expect(csv).toContain("E1");
  });
});
