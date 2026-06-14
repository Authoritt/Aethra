namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Contratos del "file store" del satélite: el central guarda/lee/borra blobs (p.ej. backups) en el
/// disco de un satélite con espacio libre, vía el canal RPC inverso (central → satélite). El
/// <c>RelativePath</c> es relativo a un directorio base acotado del satélite — el satélite sanea la
/// ruta (rechaza traversal/absolutos) antes de tocar el disco. El blob viaja como <c>byte[]</c> en un
/// solo mensaje SignalR (cap práctico ~64 MiB, el límite de recepción del hub); el central valida el
/// tamaño antes de enviar.
/// </summary>
public sealed record StoreFileRequest(string CorrelationId, string RelativePath, byte[] Content);

/// <summary>Respuesta de <see cref="StoreFileRequest"/>: ruta absoluta donde quedó y bytes escritos.</summary>
public sealed record StoreFileResponse(string CorrelationId, string StoredPath, long SizeBytes);

/// <summary>Pide leer un blob previamente guardado por su <c>RelativePath</c>.</summary>
public sealed record ReadFileRequest(string CorrelationId, string RelativePath);

/// <summary>Respuesta de <see cref="ReadFileRequest"/> con el contenido del blob.</summary>
public sealed record ReadFileResponse(string CorrelationId, byte[] Content);

/// <summary>Pide borrar un blob por su <c>RelativePath</c>. En éxito el satélite responde con un ack.</summary>
public sealed record DeleteFileRequest(string CorrelationId, string RelativePath);
