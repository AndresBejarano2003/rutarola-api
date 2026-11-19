#!/bin/sh
# Arranque del contenedor del API (servidor propio con Coolify, y cualquier otro hosting con Docker).
# Sin argumentos: aplica las migraciones y los datos base (`migrar`, idempotente) y luego arranca el API, así cada
# despliegue deja la base de datos al día sin configurar nada en Coolify. Si la migración falla, el API no arranca
# y Coolify mantiene la versión anterior. Con argumentos (p. ej. `docker run <imagen> migrar`) ejecuta solo eso.
set -e
if [ "$#" -gt 0 ]; then
  exec dotnet RutaRola.Api.dll "$@"
fi
dotnet RutaRola.Api.dll migrar
exec dotnet RutaRola.Api.dll
