import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { API_URL } from "@/lib/api";

export const dynamic = "force-dynamic";

interface MeResponse {
  email: string;
  scopes: string[];
}

async function getMe(): Promise<MeResponse | null> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
  const res = await fetch(`${API_URL}/auth/me`, {
    headers: { cookie: cookieHeader },
    cache: "no-store",
  });
  if (!res.ok) return null;
  return res.json();
}

export default async function SettingsPage() {
  const me = await getMe();
  if (!me) {
    redirect("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-6 py-12 text-zinc-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-8">
        <nav className="text-xs text-zinc-500">
          <Link href="/dashboard" className="hover:text-zinc-300">
            Dashboard
          </Link>
          <span> / </span>
          <span className="text-zinc-300">Settings</span>
        </nav>

        <header>
          <h1 className="text-3xl font-semibold">Settings</h1>
          <p className="mt-1 text-sm text-zinc-500">
            Configuracion de tu cuenta, credenciales y secretos del workspace.
          </p>
        </header>

        <section className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
          <SettingsCard
            href="/settings/api-keys"
            title="API keys"
            description="Tokens portadores para integrar herramientas externas y agentes con la API de Aethra."
            available
          />
          <SettingsCard
            href="/settings"
            title="Perfil"
            description="Email, nombre y preferencias de cuenta."
            comingSoon
          />
          <SettingsCard
            href="/settings"
            title="DataProtection key"
            description="Llave maestra que cifra tokens en reposo. Rotacion controlada."
            comingSoon
          />
        </section>

        <section className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
          <h2 className="text-sm uppercase tracking-wider text-zinc-500">
            Sesion actual
          </h2>
          <dl className="mt-3 grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
            <div>
              <dt className="text-xs uppercase tracking-wider text-zinc-500">
                Email
              </dt>
              <dd className="mt-0.5 font-mono text-zinc-200">{me.email}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-wider text-zinc-500">
                Scopes
              </dt>
              <dd className="mt-0.5 flex flex-wrap gap-1">
                {me.scopes.length === 0 && (
                  <span className="text-zinc-500">(sin scopes)</span>
                )}
                {me.scopes.map((s) => (
                  <span
                    key={s}
                    className="rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 font-mono text-[10px] text-zinc-300"
                  >
                    {s}
                  </span>
                ))}
              </dd>
            </div>
          </dl>
        </section>
      </div>
    </main>
  );
}

function SettingsCard({
  href,
  title,
  description,
  available,
  comingSoon,
}: {
  href: string;
  title: string;
  description: string;
  available?: boolean;
  comingSoon?: boolean;
}) {
  const disabled = comingSoon === true;
  const cls = disabled
    ? "block rounded-2xl border border-zinc-800 bg-zinc-900/20 p-5 opacity-60"
    : "block rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5 transition hover:border-emerald-500/40 hover:bg-zinc-900/80";

  const inner = (
    <>
      <div className="flex items-start justify-between gap-2">
        <h3 className="text-lg font-semibold text-zinc-100">{title}</h3>
        {available && (
          <span className="shrink-0 rounded-full border border-emerald-500/40 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-emerald-300">
            Activo
          </span>
        )}
        {comingSoon && (
          <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800/40 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-zinc-400">
            Pronto
          </span>
        )}
      </div>
      <p className="mt-1 text-sm text-zinc-400">{description}</p>
    </>
  );

  if (disabled) {
    return <div className={cls}>{inner}</div>;
  }
  return (
    <Link href={href} className={cls}>
      {inner}
    </Link>
  );
}
