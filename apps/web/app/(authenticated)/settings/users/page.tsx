import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { Plus, UserIcon, Users as UsersIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/layout/page-header";
import { API_URL } from "@/lib/api";
import type { UserSummary } from "@/lib/types";
import { UserRowActions } from "./UserRowActions";

export const dynamic = "force-dynamic";

async function fetchUsers(): Promise<UserSummary[] | "unauthorized" | "error"> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/api/identity/users`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (res.status === 401) return "unauthorized";
  if (res.status === 403) return "unauthorized";
  if (!res.ok) return "error";
  return (await res.json()) as UserSummary[];
}

export default async function UsersPage() {
  const data = await fetchUsers();
  if (data === "unauthorized") redirect("/login");

  const errored = data === "error";
  const users = Array.isArray(data) ? data : [];

  return (
    <div className="px-6 py-8 md:px-10 md:py-10">
      <PageHeader
        breadcrumbs={[
          { label: "Settings", href: "/settings" },
          { label: "Users" },
        ]}
        title="Usuarios"
        description="Cuentas humanas con acceso a Aethra. Cada usuario tiene uno o más roles que determinan sus permisos."
        actions={
          <div className="flex gap-2">
            <Button asChild variant="outline">
              <Link href="/settings/roles">Roles</Link>
            </Button>
            <Button asChild>
              <Link href="/settings/users/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear usuario
              </Link>
            </Button>
          </div>
        }
      />

      {errored ? (
        <Card className="border-destructive/30 bg-destructive/5">
          <CardContent className="p-4 text-sm text-destructive">
            No se pudo cargar el listado.
          </CardContent>
        </Card>
      ) : users.length === 0 ? (
        <EmptyState
          icon={<UsersIcon className="h-6 w-6" />}
          title="Aún sin usuarios"
          description="Creá tu primer usuario para empezar a invitar a tu equipo."
          action={
            <Button asChild>
              <Link href="/settings/users/new">
                <Plus className="mr-2 h-4 w-4" />
                Crear usuario
              </Link>
            </Button>
          }
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Usuario</TableHead>
                <TableHead>Roles</TableHead>
                <TableHead>Último login</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((u) => (
                <TableRow key={u.id}>
                  <TableCell className="align-top">
                    <div className="flex items-center gap-2">
                      <div className="flex h-8 w-8 items-center justify-center rounded-full bg-primary/10 text-primary">
                        <UserIcon className="h-4 w-4" />
                      </div>
                      <div className="min-w-0">
                        <div className="truncate font-medium">
                          {u.displayName ?? u.email}
                        </div>
                        <div className="truncate font-mono text-[10px] text-muted-foreground">
                          {u.email}
                        </div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="align-top">
                    <div className="flex flex-wrap gap-1">
                      {u.roles.length === 0 ? (
                        <Badge variant="outline" className="text-[10px]">
                          (sin roles)
                        </Badge>
                      ) : (
                        u.roles.map((r) => (
                          <Badge
                            key={r.id}
                            variant={
                              r.slug === "admin" ? "warning" : "outline"
                            }
                            className="text-[10px]"
                          >
                            {r.displayName}
                          </Badge>
                        ))
                      )}
                    </div>
                  </TableCell>
                  <TableCell className="align-top text-xs text-muted-foreground">
                    {formatRelative(u.lastLoginAt)}
                  </TableCell>
                  <TableCell className="align-top">
                    {u.isActive ? (
                      <Badge variant="success" className="text-[10px]">
                        Activo
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-[10px]">
                        Inactivo
                      </Badge>
                    )}
                  </TableCell>
                  <TableCell className="align-top text-right">
                    <UserRowActions
                      id={u.id}
                      email={u.email}
                      isActive={u.isActive}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}

function formatRelative(iso: string | null | undefined): string {
  if (!iso) return "nunca";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 0) return d.toLocaleString();
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "hace unos seg.";
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.floor(hours / 24);
  return `hace ${days} d`;
}
