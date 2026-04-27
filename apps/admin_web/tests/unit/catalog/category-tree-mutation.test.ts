import { describe, expect, it } from "vitest";
import {
  applyMovesLocally,
  diffMoves,
} from "@/lib/catalog/category-tree-mutation";
import type { CategoryNode } from "@/lib/api/clients/catalog";

const node = (
  id: string,
  parentId: string | null,
  order: number,
): CategoryNode => ({
  id,
  parentId,
  label: { en: id, ar: id },
  order,
  active: true,
  productCount: 0,
  childIds: [],
});

describe("category tree mutation", () => {
  it("applyMovesLocally repoints parent and order", () => {
    const before = [node("a", null, 0), node("b", null, 1)];
    const after = applyMovesLocally(before, [
      { id: "b", parentId: "a", order: 0 },
    ]);
    const moved = after.find((n) => n.id === "b")!;
    expect(moved.parentId).toBe("a");
    expect(moved.order).toBe(0);
  });

  it("diffMoves returns only changed rows", () => {
    const before = [node("a", null, 0), node("b", null, 1)];
    const after = [node("a", null, 0), node("b", "a", 0)];
    const moves = diffMoves(before, after);
    expect(moves).toEqual([{ id: "b", parentId: "a", order: 0 }]);
  });

  it("diffMoves returns empty when shapes match", () => {
    const tree = [node("a", null, 0)];
    expect(diffMoves(tree, tree)).toEqual([]);
  });
});
