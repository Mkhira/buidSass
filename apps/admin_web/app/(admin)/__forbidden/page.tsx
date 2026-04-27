/**
 * /__forbidden — registered per FR-028a (inside (admin) so a session is
 * required to see it; permission denial brings the admin here).
 */
import { ForbiddenState } from "@/components/shell/forbidden-state";

export default function ForbiddenPage() {
  return <ForbiddenState />;
}
