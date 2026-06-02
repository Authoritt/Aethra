import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import { CreatedSecretCard } from "./CreatedSecretCard";

export const dynamic = "force-dynamic";

interface PageProps {
  searchParams: Promise<{ id?: string }>;
}

async function checkAuth(): Promise<boolean> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  return res.ok;
}

export default async function ApiKeyCreatedPage({ searchParams }: PageProps) {
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  const params = await searchParams;
  const id = params.id;

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/dashboard" className="hover:text-zinc-300">
            Dashboard
          </Link>
          <span> / </span>
          <Link href="/settings" className="hover:text-zinc-300">
            Settings
          </Link>
          <span> / </span>
          <Link href="/settings/api-keys" className="hover:text-zinc-300">
            API keys
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Creada</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">API key creada</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Copia el secret ahora. Es la unica vez que podras verlo: por
            seguridad solo guardamos su hash en la base.
          </p>
        </header>

        <CreatedSecretCard id={id ?? null} />
      </div>
    </main>
  );
}
