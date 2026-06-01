import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { serverFetch } from "@/lib/server-fetch";
import type {
  ClientSummary,
  EnvironmentDefinitionDto,
  TemplateDetail,
  VmDto,
} from "@/lib/types";
import { NewInstanceForm } from "./NewInstanceForm";

export const dynamic = "force-dynamic";

export default async function NewInstancePage({
  params,
}: {
  params: Promise<{ templateId: string }>;
}) {
  const { templateId } = await params;

  const templateResult = await serverFetch<TemplateDetail>(
    `/api/templates/${templateId}`,
  );
  if (templateResult === "unauthorized") redirect("/login");
  if (templateResult === "notfound") notFound();
  if (templateResult === "error") {
    return (
      <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
        <div className="mx-auto max-w-3xl rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">
          Error cargando el template.
        </div>
      </main>
    );
  }
  const template = templateResult;

  const [clientsResult, environmentsResult, vmsResult] = await Promise.all([
    serverFetch<ClientSummary[]>(`/api/projects/${template.projectId}/clients`),
    serverFetch<EnvironmentDefinitionDto[]>(`/api/settings/environments/`),
    serverFetch<VmDto[]>(`/api/vms/`),
  ]);

  const clients = Array.isArray(clientsResult) ? clientsResult : [];
  const environments = Array.isArray(environmentsResult)
    ? [...environmentsResult].sort((a, b) => a.order - b.order)
    : [];
  const vms = Array.isArray(vmsResult) ? vmsResult : [];

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
        <nav className="text-xs text-zinc-500">
          <Link
            href={`/templates/${template.id}`}
            className="hover:text-zinc-300"
          >
            {template.name}
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nueva instance</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Nueva instance</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Despliega este template en un client + environment + VM concreto.
          </p>
        </header>

        {(clientsResult === "error" ||
          environmentsResult === "error" ||
          vmsResult === "error") && (
          <p className="rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
            No se pudieron cargar todos los catalogos. La creacion podria fallar.
          </p>
        )}

        <NewInstanceForm
          templateId={template.id}
          clients={clients}
          environments={environments}
          vms={vms}
        />
      </div>
    </main>
  );
}
