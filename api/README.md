# RutaRola — API

Backend de RutaRola: ASP.NET Core Web API (.NET 10, C#), PostgreSQL con EF Core + Npgsql (Code First),
multiempresa. Lo consumen el frontend Angular (`RutaRolaApp/`, desplegado en https://rutarola.netlify.app)
y la futura app móvil.

## Estructura

```
api/
├── RutaRola.sln
├── Directory.Build.props        net10.0, nullable (advertencias de null = error)
├── src/
│   ├── RutaRola.Api/            Program.cs, Configuracion/ (servicios, JSON, Serilog, OpenAPI), Errores/, Seguridad/, Controllers/
│   ├── RutaRola.Application/    Comun/ (IContextoUsuario, ErrorNegocio, Paginacion/ListaPaginada, Reclamos) + servicios, DTOs, validadores por módulo
│   ├── RutaRola.Domain/         Comun/ (Entidad, EntidadEmpresa, Persona), Enums/, Seguridad/ (Roles, Permisos), Personas/, Operacion/, Finanzas/, Comunicacion/
│   └── RutaRola.Infrastructure/ Persistencia/ (RutaRolaDbContext, Configuraciones/, Migraciones/, Seed/), DependencyInjection.cs
├── tests/RutaRola.Tests/        xUnit + Testcontainers (PostgreSQL real) + WebApplicationFactory
├── Dockerfile                   multi-stage, escucha en $PORT
└── docker-compose.yml           PostgreSQL 17 + API para desarrollo
```

## Correr en local

Requisitos: .NET SDK 10, Docker Desktop encendido, `dotnet-ef` (`dotnet tool install -g dotnet-ef`).

**Opción A, todo en Docker:**

```bash
cd api
cp .env.example .env        # y cambia las claves
docker compose up --build   # API en http://localhost:8080 · docs en http://localhost:8080/docs
```

En desarrollo el contenedor aplica migraciones y seed al arrancar (`BaseDatos__MigrarAlIniciar=true`).

**Opción B, API con `dotnet run` y Postgres en Docker:**

```bash
cd api
docker compose up -d db
dotnet user-secrets --project src/RutaRola.Api set "ConnectionStrings:RutaRola" "Host=localhost;Port=5432;Database=rutarola;Username=rutarola;Password=<POSTGRES_PASSWORD de .env>"
dotnet user-secrets --project src/RutaRola.Api set "Seed:Admin:Password" "<clave del admin demo>"
dotnet user-secrets --project src/RutaRola.Api set "Jwt:Clave" "$(openssl rand -base64 48)"
dotnet run --project src/RutaRola.Api            # http://localhost:5080/docs
```

Usuario demo: `admin` (o `admin@rutarola.com`) con la clave de `Seed__Admin__Password`, rol AdminEmpresa
de la empresa "RutaRola Demo".

### Comandos frecuentes

```bash
dotnet build                                   # desde api/
dotnet test                                    # requiere Docker (Testcontainers levanta un Postgres)
dotnet test --filter "FullyQualifiedName~Multiempresa"

# Nueva migración (después de cambiar entidades o configuraciones)
dotnet ef migrations add <NombreEnPascalCase> -p src/RutaRola.Infrastructure -s src/RutaRola.Infrastructure -o Persistencia/Migraciones
# Aplicar migraciones + seed (local o en el despliegue)
dotnet run --project src/RutaRola.Api -- migrar
```

## Variables de entorno

| Variable | Obligatoria | Descripción |
|---|---|---|
| `ConnectionStrings__RutaRola` | sí | Cadena de conexión de PostgreSQL (`Host=…;Port=5432;Database=…;Username=…;Password=…;SSL Mode=Require` en la nube). |
| `ASPNETCORE_ENVIRONMENT` | — | `Production` en la imagen. Fuera de producción se publican `/openapi/v1.json` y `/docs` (Scalar). |
| `PORT` | — | Puerto HTTP (8080 en la imagen). |
| `Cors__Origenes` | — | Orígenes permitidos separados por comas. Por defecto `http://localhost:4200,https://rutarola.netlify.app`. |
| `Seed__Admin__Password` | para crear el admin | Clave del admin demo. Sin ella no se crea. Nunca se sobrescribe la de un usuario existente. |
| `Seed__Admin__Usuario`, `Seed__Admin__Email` | — | Por defecto `admin` / `admin@rutarola.com`. |
| `Seed__SuperAdmin__Password` | — | Si se define, se crea `superadmin` (plataforma, sin empresa). |
| `Seed__Empresa__Nombre`, `Seed__Empresa__Nit` | — | Empresa demo (por defecto "RutaRola Demo", `900000001-1`). |
| `BaseDatos__MigrarAlIniciar` | — | `true` aplica migraciones y seed al arrancar. **Se ignora en Production.** |
| `Jwt__Clave` | sí | Clave HMAC para firmar los JWT, mínimo 32 caracteres (`openssl rand -base64 48`). El API no arranca sin ella. |
| `Jwt__MinutosAcceso`, `Jwt__DiasRefresh` | — | Vida del access token (15) y del refresh token (30 días). |
| `Jwt__SegundosGraciaRotacion` | — | Gracia (30 s) para dos renovaciones simultáneas del mismo refresh (p. ej. dos pestañas). |
| `Seguridad__IntentosAutenticacionPorMinuto` | — | Límite por IP para `/auth/login` y `/auth/refresh` (10). |

No se guardan secretos en el repo: en desarrollo `dotnet user-secrets` o `api/.env` (ignorado por git);
en el hosting, sus variables de entorno.

## Desplegar

1. Construir la imagen: `docker build -t rutarola-api ./api`.
2. **Migrar (paso explícito, antes de cada versión):**
   `docker run --rm -e ConnectionStrings__RutaRola="…" -e Seed__Admin__Password="…" rutarola-api migrar`
   (en Render o Railway: "pre-deploy command" `dotnet RutaRola.Api.dll migrar`). Es idempotente.
3. Arrancar el contenedor con las variables de entorno. Escucha en `$PORT`, confía en `X-Forwarded-For/Proto`
   del proxy del hosting y expone `GET /health` (200 si la BD responde, 503 si no) para el health check.
4. En Netlify, el frontend apunta al API con `environment.prod.ts → apiUrl = https://<api>/api/v1` (fase 2).

## Autenticación y permisos

| Endpoint | Descripción |
|---|---|
| `POST /api/v1/auth/login` `{usuario, contrasena}` | `usuario` = usuario de ingreso **o** correo. Responde `{ sesion, tokens }`: `sesion` tiene la forma de `Sesion` del frontend (`rol` ADMINISTRADOR, CONDUCTOR, MONITORA, ACUDIENTE o SUPER_ADMIN; `empleadoId`/`acudienteId`; `empresa`; `permisos`). 401 `Usuario o contraseña incorrectos.`; 403 si el usuario, la empresa o la persona vinculada están inactivos. |
| `POST /api/v1/auth/refresh` `{refreshToken}` | Responde `tokens` nuevos; el refresh anterior deja de servir (rotación). Reusar uno ya rotado revoca todas las sesiones del usuario (posible robo). |
| `POST /api/v1/auth/logout` `{refreshToken?}` | Revoca ese refresh y la sesión del access token, si viene. Responde 204 y es idempotente. |
| `GET /api/v1/auth/me` | Sesión actual con los permisos vigentes. |

- **Access token:** JWT HS256 de 15 minutos con los claims `sub`, `sid` (sesión), `name`, `rol`, `id_empresa`, `id_empleado` / `id_acudiente` y un `permiso` por permiso.
  - Se envía como `Authorization: Bearer`.
  - Al cerrar sesión, el access token sigue vivo hasta que expira (máximo 15 min).
- **Refresh token:** 256 bits aleatorios. En `sesiones_usuario` se guarda solo su SHA-256, con IP, dispositivo y vencimiento.
- **Seguro por defecto:** todo endpoint exige sesión (fallback policy) salvo `[AllowAnonymous]`: login, refresh, logout, `/health` y la documentación.
  - Un 401 o 403 también llega como ProblemDetails con `mensaje`.
- **Permisos:** cada permiso de `Domain/Seguridad/Catalogo.cs` es una policy y se exige con `[RequierePermiso(Permisos.X)]`. Los permisos de cada rol están en `Permisos.PorRol`.
- **Plataforma:** el SuperAdmin (sin empresa) crea empresas con `POST /api/v1/empresas`, con su configuración por defecto y, opcionalmente, su primer `administrador`.
  - Cada empresa ve y edita la suya en `/api/v1/empresas/actual`.
  - Su configuración está en `GET/PUT /api/v1/configuracion/{clave}`.

## Convenciones

- **Rutas:** `/api/v1/<recurso-en-plural-y-en-español>` en minúsculas (`/api/v1/estudiantes`,
  `/api/v1/rutas/{id}/estudiantes`, `/api/v1/recorridos/{id}/paradas`). `GET /health` va fuera de la versión.
- **Contrato del frontend:** cada `RutaRolaApp/src/app/**/<plural>-http.ts` documenta el recurso, los filtros y
  las reglas 409/422. El API los respeta para que el frontend pase a modo HTTP cambiando solo
  `core/repositorios.ts`.
  - CRUD: `GET /x?filtros&pagina&tamano` → `{ items, total, pagina, tamano }`, `GET /x/{id}`, `POST`,
    `PUT /x/{id}`, `DELETE /x/{id}` y `GET /x/opciones?filtros` → `[{ id, nombre }]`.
- **DTOs siempre:** nunca se devuelve una entidad de EF. Los nombres de los DTOs son los del frontend
  (`acudienteId`, `valorContratacion`, `vencimientoSoat`…), aunque la columna se llame distinto.
- **JSON:** camelCase; enums como texto `UPPER_SNAKE` (`EN_CURSO`, `A_BORDO`); fechas `YYYY-MM-DD`; horas `HH:mm`;
  periodos `YYYY-MM`; booleanos de estado como `"ACTIVO"`/`"INACTIVO"`; ids UUID como texto.
- **Búsquedas por texto:** sin tildes ni mayúsculas, como `normalizar()` del frontend.
  - Se escriben `FuncionesBd.SinTildes(columna).Contains(Busqueda.Normalizar(texto))`.
  - En SQL es la función `f_sin_tildes` (extensión `unaccent`, creada en la migración `Fase2Autenticacion`).
- **Validación:** un `AbstractValidator<T>` por solicitud. Lo aplica `FiltroValidacion` a todo argumento de una acción; no hace falta llamarlo a mano.
  - `WithMessage` va justo después de la regla a la que aplica.
- **Errores:** ProblemDetails (`application/problem+json`) con `mensaje` en español (el frontend lo muestra tal
  cual), `traceId` y `errores` por campo en las validaciones.
  - 400: validación (FluentValidation).
  - 404: no existe, o es de otra empresa.
  - 409: duplicado, en uso o concurrencia.
  - 422: regla de negocio.
  - 401/403: sin sesión o sin permiso.
- **Base de datos:**
  - snake_case (EFCore.NamingConventions), tablas en plural, PK `id_<entidad>` (UUID v7; `gps_vehiculos`,
    `auditoria` y `eventos` usan bigint identity), FK `id_<padre>` siempre con índice.
  - Dinero `numeric(14,2)`, coordenadas `numeric(9,6)`, fechas y horas en `timestamptz` UTC.
  - Enums como texto `UPPER_SNAKE`; sin cascada por defecto (solo en hijos de composición).
- **Multiempresa:**
  - Toda entidad con `IdEmpresa` implementa `IEntidadEmpresa`. Las hijas (cursos, paradas, detalle…) se filtran por su padre.
  - `RutaRolaDbContext` aplica el query filter `"Empresa"` con la empresa del token. Sin empresa no se ve nada.
  - Al guardar asigna la empresa del token a lo nuevo y rechaza (403) cambios sobre registros de otra empresa.
  - `UsarModoSistema()` solo para el seed y los procesos del servidor.
  - **Ojo:** el filtro no impide *referenciar* un id ajeno. Al recibir un id de otra entidad (`colegioId`…),
    el servicio debe buscarlo con el filtro activo y responder 404 si no aparece.
- **Borrado:** `DELETE` elimina físicamente solo si nada referencia el registro. Si está en uso responde 409
  con `{mensaje}`, y el frontend sugiere marcarlo Inactivo (que es el borrado lógico). Las notificaciones
  usan `eliminada` para no repetir un hecho ya notificado.
- **Seguridad:**
  - Roles base: SuperAdmin, AdminEmpresa (sale como `ADMINISTRADOR` en la sesión), Conductor, Monitora y Acudiente.
  - Permisos `modulo.accion` en `Domain/Seguridad/Catalogo.cs`; el seed los sincroniza.
  - Contraseñas con `PasswordHasher` de ASP.NET Core Identity.
  - Refresh tokens: solo su hash en `sesiones_usuario`.
  - El log de peticiones no registra query strings ni cuerpos (datos de menores).

## Diferencias con el modelo de Lucidchart (`docs/modelo-bd-rutarola.json`)

Se aplicaron para que la BD sirva exactamente lo que usa el frontend:

- `conductores` pasó a `empleados`, con `tipo_empleado` CONDUCTOR/MONITORA, documento, nombres, contrato y
  licencia. El teléfono vive en el empleado; `usuarios` solo guarda credenciales y rol.
- `usuarios.usuario` es el usuario de ingreso, único global; `email` es opcional y único.
- `estudiantes_acudientes` es N:M con `es_principal` (el `acudienteId` del frontend es el principal). Se quitó
  `estudiantes.id_acudiente`. `acudientes.id_usuario` da acceso al portal.
- Tablas nuevas:
  - `colegios_cursos`;
  - `asignaciones_ruta` (vehículo + conductor + monitora con vigencia), por lo que `rutas` ya no tiene vehículo ni conductor;
  - `cotizaciones` (+ `cotizaciones_estudiantes`);
  - `programaciones_cobro`, `plantillas_mensaje` (+ `plantillas_mensaje_archivos`) y `envios_programacion`.
- `viajes_ruta` = recorrido: asignación, trayecto, monitora, snapshot, orden actualizado y `xmin` para
  concurrencia. `paradas_viaje` lleva el snapshot del estudiante y del acudiente.
- `facturas` = contrato mensual: acudiente, periodos, valor mensual, snapshot. `facturas_detalle` tiene una
  línea por mes.
- `pagos` = recibos: número, periodo, `id_factura`, snapshot, y uno vigente por factura + mes.
- `gps_vehiculos.direccion` pasó a `rumbo`.
- `sesiones_usuario` guarda `token_hash`, `revocado` y `id_reemplazada_por`.
- `auditoria` tiene `id_empresa` y valores en `jsonb`.
- `notificaciones` tiene `clave`, `categoria`, `nivel`, `leida`, `archivada`, `eliminada` y enlace.
- `vehiculos` tiene `numero_interno`, `tipo` y `linea`. Los vencimientos van en `documentos_vehiculo`, uno por tipo.

## Fases

1. ✅ Base: solución, entidades (36 tablas), configuraciones, migración `Inicial`, docker-compose, seed, health, errores, logs, OpenAPI.
2. ✅ Auth (JWT 15 min + refresh rotativo), permisos por policy, empresas y configuración, aislamiento A/B por HTTP, interceptor en Angular.
3. Catálogos: colegios, vehículos, empleados/conductores, acudientes, estudiantes (+ auditoría).
4. Operación: rutas, asignaciones, recorridos, paradas, eventos.
5. GPS: lotes de posiciones y última posición (SignalR preparado).
6. Finanzas: facturas, recibos, cotizaciones, gastos, reportes, tablero.
7. Comunicación: notificaciones, programaciones/envíos, registro de WhatsApp, portal.
