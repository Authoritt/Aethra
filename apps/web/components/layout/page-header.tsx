import * as React from "react";
import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";

export interface PageHeaderBreadcrumb {
  label: string;
  href?: string;
}

export interface PageHeaderProps {
  title: string;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  /**
   * Breadcrumbs opcionales. El `AppTopbar` ya muestra breadcrumbs autogenerados
   * a partir del pathname; pásalos aquí solo cuando quieras una variante
   * embellecida o jerarquía no inferible.
   */
  breadcrumbs?: PageHeaderBreadcrumb[];
  className?: string;
}

/**
 * Encabezado estándar de page. Layout: título grande + descripción muted a la
 * izquierda, slots de acciones (botones/links) a la derecha. En mobile las
 * acciones envuelven debajo del título.
 */
export function PageHeader({
  title,
  description,
  actions,
  breadcrumbs,
  className,
}: PageHeaderProps) {
  return (
    <header className={cn("flex flex-col gap-4 pb-6", className)}>
      {breadcrumbs && breadcrumbs.length > 0 ? (
        <nav aria-label="Breadcrumb" className="text-xs text-muted-foreground">
          <ol className="flex flex-wrap items-center gap-1.5">
            {breadcrumbs.map((bc, idx) => {
              const isLast = idx === breadcrumbs.length - 1;
              return (
                <li key={`${bc.label}-${idx}`} className="flex items-center gap-1.5">
                  {bc.href && !isLast ? (
                    <Link
                      href={bc.href}
                      className="hover:text-foreground transition-colors"
                    >
                      {bc.label}
                    </Link>
                  ) : (
                    <span
                      className={cn(
                        isLast ? "text-foreground font-medium" : undefined,
                      )}
                    >
                      {bc.label}
                    </span>
                  )}
                  {!isLast ? (
                    <ChevronRight className="h-3 w-3 opacity-50" aria-hidden />
                  ) : null}
                </li>
              );
            })}
          </ol>
        </nav>
      ) : null}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
        <div className="min-w-0 space-y-1.5">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {title}
          </h1>
          {description ? (
            <p className="max-w-3xl text-sm text-muted-foreground">
              {description}
            </p>
          ) : null}
        </div>
        {actions ? (
          <div className="flex flex-wrap items-center gap-2">{actions}</div>
        ) : null}
      </div>
    </header>
  );
}
