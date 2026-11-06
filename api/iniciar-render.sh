#!/bin/sh
# Arranque en Render (plan gratuito, sin "pre-deploy command"): paso explícito `migrar`
# (migraciones + datos base, idempotente) y luego el API. Si la migración falla, el API no arranca.
set -e
dotnet RutaRola.Api.dll migrar
exec dotnet RutaRola.Api.dll
