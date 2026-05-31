/**
 * Helper para llamar al backend de Aethra. Envía cookies automáticamente
 * y normaliza errores en `ApiError`.
 *
 * URL base configurable vía `NEXT_PUBLIC_API_URL` (por defecto http://localhost:5080).
 */

// TODO F9.3+: añadir helpers para templates, clients, instances, builds, deployments.

export const API_URL =
  process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

export class ApiError extends Error {
  constructor(public status: number, public body: unknown, message?: string) {
    super(message ?? `API error ${status}`);
    this.name = "ApiError";
  }
}

export async function api<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const headers = new Headers(init.headers);
  if (!headers.has("Content-Type") && init.body) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers,
    credentials: "include",
  });

  const text = await response.text();
  const data = text ? safeJson(text) : null;

  if (!response.ok) {
    throw new ApiError(response.status, data, `API ${response.status} en ${path}`);
  }
  return data as T;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}
