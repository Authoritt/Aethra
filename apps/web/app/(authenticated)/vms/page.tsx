import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Plus, Server } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { PageHeader } from "@/components/layout/page-header";
import { VmStatusPill } from "@/components/aethra/vm-status-pill";
import { API_URL } from "@/lib/api";
import type { VmDto } from "@/lib/types";

export const dynamic = "force-dynamic";

async function fetchVms(): Promise<VmDto[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/vms/`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as VmDto[];
}

export default async function VmsPage() {
  const t = await getTranslations("pages.vms_list");
  const tCommon = await getTranslations("common");
  const data = await fetchVms();
  if (data === "unauthorized") {
    redirect("/login");
  }

  const errored = data === "error";
  const vms = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Button asChild>
            <Link href="/vms/new">
              <Plus className="mr-2 h-4 w-4" />
              {t("register_vm")}
            </Link>
          </Button>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            {tCommon("load_error")}
          </CardContent>
        </Card>
      ) : vms.length === 0 ? (
        <EmptyState
          icon={<Server className="h-6 w-6" />}
          title={t("empty_title")}
          description={t("empty_description")}
          action={
            <Button asChild>
              <Link href="/vms/new">
                <Plus className="mr-2 h-4 w-4" />
                {t("register_vm")}
              </Link>
            </Button>
          }
        />
      ) : (
        <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
          {vms.map((vm) => (
            <li key={vm.id}>
              <Link href={`/vms/${vm.id}`} className="group block h-full">
                <Card className="h-full transition-colors group-hover:border-primary/40">
                  <CardContent className="space-y-3 p-5">
                    <div className="flex items-start justify-between gap-3">
                      <h3 className="truncate text-base font-semibold text-foreground">
                        {vm.name}
                      </h3>
                      <VmStatusPill status={vm.status} />
                    </div>
                    <p className="font-mono text-xs text-muted-foreground">
                      {vm.slug}
                    </p>
                    {vm.description ? (
                      <p className="line-clamp-2 text-sm text-muted-foreground">
                        {vm.description}
                      </p>
                    ) : null}

                    <dl className="grid grid-cols-2 gap-2 pt-2 text-xs">
                      <Stat
                        label={t("label_publicIp")}
                        value={vm.publicIp ?? "—"}
                        mono
                      />
                      <Stat
                        label={t("label_cpu")}
                        value={vm.cpuCores ? `${vm.cpuCores} cores` : "—"}
                      />
                      <Stat
                        label={t("label_ram")}
                        value={formatGb(vm.totalMemoryBytes)}
                      />
                      <Stat
                        label={t("label_agent")}
                        value={vm.agentVersion ?? "—"}
                        mono
                      />
                    </dl>
                  </CardContent>
                </Card>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Stat({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div>
      <dt className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </dt>
      <dd
        className={`mt-0.5 truncate text-foreground ${mono ? "font-mono" : ""}`}
        title={value}
      >
        {value}
      </dd>
    </div>
  );
}

function formatGb(bytes: number | null) {
  if (!bytes) return "—";
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`;
}
