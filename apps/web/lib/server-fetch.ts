/**
 * Helper para fetch desde server components reenviando las cookies del browser
 * (en particular `aethra.sid`). Centraliza el patron para evitar duplicar
 * la construccion del header Cookie en cada page.
 */

import { cookies } from "next/headers";
import { API_URL } from "@/lib/api";

export type ServerFetchResult<T> = T | "unauthorized" | "notfound" | "error";

export async function buildCookieHeader(): Promise<string> {
  const cookieStore = await cookies();
  return cookieStore
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join("; ");
}

export async function serverFetch<T>(
  path: string,
  init?: RequestInit,
): Promise<ServerFetchResult<T>> {
  const cookieHeader = await buildCookieHeader();
  const headers = new Headers(init?.headers);
  headers.set("cookie", cookieHeader);
  if (!headers.has("Content-Type") && init?.body) {
    headers.set("Content-Type", "application/json");
  }

  const res = await fetch(`${API_URL}${path}`, {
    ...init,
    headers,
    cache: "no-store",
  });

  if (res.status === 401) return "unauthorized";
  if (res.status === 404) return "notfound";
  if (!res.ok) return "error";

  const text = await res.text();
  if (!text) return null as unknown as T;
  try {
    return JSON.parse(text) as T;
  } catch {
    return text as unknown as T;
  }
}
