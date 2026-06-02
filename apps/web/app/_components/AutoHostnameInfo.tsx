import { cn } from "@/lib/utils";

/**
 * Badge informativo que muestra el auto-hostname asignado por Aethra
 * (template-client-env.base_domain). Si no hay autoHostname, muestra
 * un placeholder neutro. Cuando hay customDomain configurado, se
 * indica que el auto-hostname sigue activo pero ya no es el principal.
 */
export function AutoHostnameInfo({
  autoHostname,
  customDomain,
}: {
  autoHostname: string | null;
  customDomain?: string | null;
}) {
  if (!autoHostname) {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-md border border-border bg-muted px-2 py-1 font-mono text-[11px] text-muted-foreground">
        sin hostname asignado
      </span>
    );
  }

  const overridden = Boolean(customDomain);
  return (
    <span
      className={cn(
        "inline-flex items-center gap-2 rounded-md border px-2 py-1 font-mono text-[11px]",
        overridden
          ? "border-border bg-muted text-muted-foreground"
          : "border-success/30 bg-success/5 text-success-foreground",
      )}
      title={
        overridden
          ? "El custom domain tiene prioridad. El auto-hostname sigue funcionando como alias."
          : "Hostname autogenerado: template-client-environment.base_domain"
      }
    >
      <span
        className={cn(
          "size-1.5 rounded-full",
          overridden ? "bg-muted-foreground" : "bg-success",
        )}
      />
      {autoHostname}
      {overridden ? (
        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">
          alias
        </span>
      ) : null}
    </span>
  );
}
