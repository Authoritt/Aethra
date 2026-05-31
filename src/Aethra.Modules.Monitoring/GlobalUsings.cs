// Alias para evitar colisión con System.Threading.Monitor en archivos que importen
// implícitamente System.Threading via ImplicitUsings. "Monitor" sin alias siempre se
// refiere al agregado del módulo.
global using Monitor = Aethra.Modules.Monitoring.Domain.Monitor;
