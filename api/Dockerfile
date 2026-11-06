# syntax=docker/dockerfile:1
# Imagen del API (multi-stage). Escucha en $PORT (8080 por defecto).
# Migraciones (paso explícito del despliegue):  docker run --rm -e ConnectionStrings__RutaRola=... <imagen> migrar

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props RutaRola.sln ./
COPY src/RutaRola.Domain/RutaRola.Domain.csproj src/RutaRola.Domain/
COPY src/RutaRola.Application/RutaRola.Application.csproj src/RutaRola.Application/
COPY src/RutaRola.Infrastructure/RutaRola.Infrastructure.csproj src/RutaRola.Infrastructure/
COPY src/RutaRola.Api/RutaRola.Api.csproj src/RutaRola.Api/
RUN dotnet restore src/RutaRola.Api/RutaRola.Api.csproj
COPY src/ src/
RUN dotnet publish src/RutaRola.Api/RutaRola.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV PORT=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
COPY --from=build /app .
COPY --chmod=755 iniciar-render.sh /app/iniciar-render.sh
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "RutaRola.Api.dll"]
