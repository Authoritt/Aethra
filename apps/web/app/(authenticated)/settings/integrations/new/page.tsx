import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";
import { CreateIntegrationForm } from "./CreateIntegrationForm";

export const dynamic = "force-dynamic";

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

export default async function NewIntegrationPage() {
  const ok = await checkAuth();
  if (!ok) redirect("/login");

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-2xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/dashboard" className="hover:text-zinc-300">
            Dashboard
          </Link>
          <span> / </span>
          <Link href="/settings" className="hover:text-zinc-300">
            Settings
          </Link>
          <span> / </span>
          <Link
            href="/settings/integrations"
            className="hover:text-zinc-300"
          >
            Integraciones
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Nueva</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Nueva credencial</h1>
          <p className="mt-1 text-sm text-zinc-500">
            El valor en texto plano se cifra con DataProtection y solo se
            muestra esta vez. Si lo olvidas tendras que rotar la credencial.
          </p>
        </header>

        <CreateIntegrationForm />
      </div>
    </main>
  );
}
