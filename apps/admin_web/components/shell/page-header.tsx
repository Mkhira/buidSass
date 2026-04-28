/**
 * T039: PageHeader — title + optional subtitle + right-aligned action slot.
 *
 * Feature pages mount `<AuditForResourceLink>` (T040e) inside the actions
 * slot per FR-028f.
 */
import type { ReactNode } from "react";

interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <header className="flex items-start justify-between gap-ds-md py-ds-md">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {description ? (
          <p className="mt-ds-xs text-sm text-muted-foreground">{description}</p>
        ) : null}
      </div>
      {actions ? <div className="flex items-center gap-ds-sm">{actions}</div> : null}
    </header>
  );
}
