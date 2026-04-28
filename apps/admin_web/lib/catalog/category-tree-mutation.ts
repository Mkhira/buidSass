/**
 * T008 — SM-3 (category-tree micro-state for optimistic reorder).
 *
 * The tree mutates locally (optimistic) → fires `categories.reorder` →
 * either commits or rolls back. This module captures that micro-state
 * so the consuming component can render a "saving…" indicator and revert
 * on server failure.
 */
import type { CategoryNode } from "@/lib/api/clients/catalog";

export type ReorderMicroState =
  | { kind: "idle" }
  | { kind: "draft"; pendingMoves: ReorderMove[] }
  | { kind: "committing"; moves: ReorderMove[] }
  | { kind: "committed" }
  | { kind: "rolled_back"; reason: string };

export interface ReorderMove {
  id: string;
  parentId: string | null;
  order: number;
}

export function applyMovesLocally(
  tree: CategoryNode[],
  moves: ReorderMove[],
): CategoryNode[] {
  const byId = new Map(tree.map((n) => [n.id, { ...n }]));
  for (const move of moves) {
    const node = byId.get(move.id);
    if (!node) continue;
    node.parentId = move.parentId;
    node.order = move.order;
  }
  return [...byId.values()];
}

export function diffMoves(
  before: CategoryNode[],
  after: CategoryNode[],
): ReorderMove[] {
  const beforeById = new Map(before.map((n) => [n.id, n]));
  const moves: ReorderMove[] = [];
  for (const n of after) {
    const prev = beforeById.get(n.id);
    if (!prev) continue;
    if (prev.parentId !== n.parentId || prev.order !== n.order) {
      moves.push({ id: n.id, parentId: n.parentId, order: n.order });
    }
  }
  return moves;
}
