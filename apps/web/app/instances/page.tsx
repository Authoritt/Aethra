import { redirect } from "next/navigation";

export const dynamic = "force-dynamic";

export default async function InstancesIndexPage() {
  // Las instances viven dentro de un template (creacion) o se acceden via
  // su id en /instances/{id}. No hay listado global aqui.
  redirect("/projects");
}
