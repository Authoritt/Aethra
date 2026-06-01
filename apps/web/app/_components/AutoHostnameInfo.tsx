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
      <span className="inline-flex items-center gap-1.5 rounded-md border border-zinc-800 bg-zinc-900/40 px-2 py-1 font-mono text-[11px] text-zinc-500">
        sin hostname asignado
      </span>
    );
  }

  const overridden = Boolean(customDomain);
  return (
    <span
      className={`inline-flex items-center gap-2 rounded-md border px-2 py-1 font-mono text-[11px] ${
        overridden
          ? "border-zinc-800 bg-zinc-900/40 text-zinc-400"
          : "border-emerald-500/30 bg-emerald-500/5 text-emerald-200"
      }`}
      title={
        overridden
          ? "El custom domain tiene prioridad. El auto-hostname sigue funcionando como alias."
          : "Hostname autogenerado: template-client-environment.base_domain"
      }
    >
      <span
        className={`size-1.5 rounded-full ${
          overridden ? "bg-zinc-500" : "bg-emerald-400"
        }`}
      />
      {autoHostname}
      {overridden && (
        <span className="text-[10px] uppercase tracking-wider text-zinc-500">alias</span>
      )}
    </span>
  );
}
