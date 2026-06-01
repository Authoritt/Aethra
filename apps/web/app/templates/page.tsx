import { redirect } from "next/navigation";

export const dynamic = "force-dynamic";

export default async function TemplatesIndexPage() {
  // Los templates viven dentro de un project. No hay un listado global;
  // se navega desde /projects/{id}.
  redirect("/projects");
}
