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
dotnet run --project src/RutaRola.Api            # http://localhost:8080/docs
```

Usuario demo: `admin` (o `admin@rutarola.com`) con la clave de `Seed__Admin__Password`, rol AdminEmpresa
de la empresa "RutaRola Demo".

**Datos de ejemplo (solo desarrollo):** con `Seed:Demo:Habilitado` (activo en `appsettings.Development.json`)
y `Seed__Demo__Password` (`SEED_DEMO_PASSWORD` en `.env`), el seed crea una vez los mismos datos del modo
local del frontend: 5 colegios de Neiva con sus cursos, 5 vehículos, 8 empleados, 8 acudientes, 12
estudiantes, 4 rutas (R-01…R-04) y 3 asignaciones (R-04 sin asignar; fecha fin vacía = indefinida), 6 facturas
(febrero–noviembre del año en curso) con sus recibos hasta el último mes pagado (FAC-0002, -0004 y -0006 quedan en
mora), 4 cotizaciones y 3 programaciones de cobro con envíos de ejemplo (uno FALLIDO). Cada sección se crea
una sola vez (si la empresa no tiene colegios, rutas o facturas): una base de una fase anterior recibe solo lo nuevo. Las fechas de creación
son históricas (enero del año en curso; recibos en su fecha de pago), como en el modo local, para que la campana y
el tablero no los traten como registros nuevos. Los empleados y acudientes ingresan con inicial del nombre + primer apellido (`jcardenas`
conductor, `ycharry` monitora, `mperez` acudiente…) y esa contraseña. En la Opción B:
`dotnet user-secrets --project src/RutaRola.Api set "Seed:Demo:Password" "<clave>"`.

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
| `ConnectionStrings__RutaRola` | sí | Cadena de conexión de PostgreSQL: formato Npgsql (`Host=…;Port=5432;Database=…;Username=…;Password=…;SSL Mode=Require`) o la URL del proveedor (`postgresql://usuario:clave@host/base?sslmode=require`, Render/Neon), que `CadenaConexion` convierte. |
| `ASPNETCORE_ENVIRONMENT` | — | `Production` en la imagen. Fuera de producción se publican `/openapi/v1.json` y `/docs` (Scalar). |
| `PORT` | — | Puerto HTTP (8080 en la imagen). |
| `Cors__Origenes` | — | Orígenes permitidos separados por comas. Por defecto `http://localhost:4200,https://rutarola.netlify.app`. |
| `Seed__Admin__Password` | para crear el admin | Clave del admin demo. Sin ella no se crea. Nunca se sobrescribe la de un usuario existente. |
| `Seed__Admin__Usuario`, `Seed__Admin__Email` | — | Por defecto `admin` / `admin@rutarola.com`. |
| `Seed__SuperAdmin__Password` | — | Si se define, se crea `superadmin` (plataforma, sin empresa). |
| `Seed__Empresa__Nombre`, `Seed__Empresa__Nit` | — | Empresa demo (por defecto "RutaRola Demo", `900000001-1`). |
| `Seed__Demo__Habilitado`, `Seed__Demo__Password` | — | Datos de ejemplo (ficticios) y clave de sus usuarios. En Production se ignoran salvo con `Seed__Demo__PermitirEnProduccion=true`, que es solo para un despliegue de **demostración** (`render.yaml`); nunca con datos de clientes. |
| `BaseDatos__MigrarAlIniciar` | — | `true` aplica migraciones y seed al arrancar. **Se ignora en Production.** |
| `Jwt__Clave` | sí | Clave HMAC para firmar los JWT, mínimo 32 caracteres (`openssl rand -base64 48`). El API no arranca sin ella. |
| `Jwt__MinutosAcceso`, `Jwt__DiasRefresh` | — | Vida del access token (15) y del refresh token (30 días). |
| `Jwt__SegundosGraciaRotacion` | — | Gracia (30 s) para dos renovaciones simultáneas del mismo refresh (p. ej. dos pestañas). |
| `Seguridad__IntentosAutenticacionPorMinuto` | — | Límite por IP para `/auth/login` y `/auth/refresh` (10). |
| `Procesos__Habilitado`, `Procesos__IntervaloMinutos` | — | Proceso de cobros y notificaciones (`true`, cada 15 min). Con varias instancias, habilitarlo en **una sola**. |
| `Mensajeria__Whatsapp`, `Mensajeria__Correo` | — | `Deshabilitado` (por defecto: no se envía ni se registra nada) o `Simulado` (desarrollo y demo: queda como entregado sin salir del servidor). Aún no hay proveedor real. |
| `Gps__DiasRetencion` | — | Días que se conservan las ubicaciones GPS (30; `0` = siempre). El proceso del servidor borra las más antiguas de todas las empresas: son posiciones de un vehículo con menores. |

No se guardan secretos en el repo: en desarrollo `dotnet user-secrets` o `api/.env` (ignorado por git);
en el hosting, sus variables de entorno.

## Desplegar

**Render (Blueprint `render.yaml` en la raíz del repo `rutarola-api`)**: PostgreSQL gratuito + servicio web Docker
(`api/Dockerfile`, contexto `api/`), health check `/health`, `Jwt__Clave` generada por Render y CORS para
`https://rutarola.netlify.app`. Al crear el Blueprint, Render pide los secretos `sync: false`
(`Seed__Admin__Password`, `Seed__SuperAdmin__Password`, `Seed__Demo__Password`). El plan gratuito no tiene
"pre-deploy command": el contenedor corre `migrar` (migraciones + seed, idempotente) y luego el API
(`dockerCommand: /app/iniciar-render.sh`; Render no interpreta comillas en ese campo, por eso es un script); con un plan pago, mover `migrar` a `preDeployCommand`. El servicio gratuito se duerme tras
15 min sin uso (el primer pedido tarda ~1 min) y su Postgres gratuito vence a los 30 días: para algo estable,
plan pago o Neon (`ConnectionStrings__RutaRola` = su URL).

Otro hosting:

1. Construir la imagen: `docker build -t rutarola-api ./api`.
2. **Migrar (paso explícito, antes de cada versión):**
   `docker run --rm -e ConnectionStrings__RutaRola="…" -e Seed__Admin__Password="…" rutarola-api migrar`
   (o el "pre-deploy command" `dotnet RutaRola.Api.dll migrar` del hosting). Es idempotente.
3. Arrancar el contenedor con las variables de entorno. Escucha en `$PORT`, confía en `X-Forwarded-For/Proto`
   del proxy del hosting y expone `GET /health` (200 si la BD responde, 503 si no) para el health check.
4. En el frontend: `src/environments/environment.ts` → `apiUrl = https://<api>/api/v1` y `usarApi: true`,
   y agregar el dominio de Netlify a `Cors__Origenes` si cambia. El frontend llama al API directo (no por un
   proxy de Netlify) porque el hub SignalR necesita WebSocket.

**Una sola instancia.** El proceso de cobros/notificaciones/retención (`Procesos__Habilitado`) y SignalR corren en
la instancia del API. Para escalar a varias: dejar `Procesos__Habilitado=true` en una sola y agregar un backplane
de SignalR (Redis con `AddStackExchangeRedis` o Azure SignalR Service); sin él, un aviso solo llega a los clientes
conectados a la instancia que lo generó.

## Autenticación y permisos

| Endpoint | Descripción |
|---|---|
| `POST /api/v1/auth/login` `{usuario, contrasena}` | `usuario` = usuario de ingreso **o** correo. Responde `{ sesion, tokens }`: `sesion` tiene la forma de `Sesion` del frontend (`rol` ADMINISTRADOR, CONDUCTOR, MONITORA, ACUDIENTE o SUPER_ADMIN; `empleadoId`/`acudienteId`; `empresa`; `permisos`). 401 `Usuario o contraseña incorrectos.`; 403 si el usuario, la empresa o la persona vinculada están inactivos. |
| `POST /api/v1/auth/refresh` `{refreshToken}` | Responde `tokens` nuevos; el refresh anterior deja de servir (rotación). Reusar uno ya rotado revoca todas las sesiones del usuario (posible robo). |
| `POST /api/v1/auth/logout` `{refreshToken?}` | Revoca ese refresh y la sesión del access token, si viene. Responde 204 y es idempotente. |
| `GET /api/v1/auth/me` | Sesión actual con los permisos vigentes. |
| `PUT /api/v1/auth/contrasena` `{actual, nueva}` | Cambia la contraseña propia (cualquier rol): 422 `La contraseña actual no es correcta.` o si es igual, 400 si la nueva tiene menos de 6. Revoca las demás sesiones del usuario (la actual sigue); la auditoría registra `(cambiada)`. 204. |

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

## Catálogos (fase 3)

Todos con el contrato CRUD (ver Convenciones) y solo para el administrador de la empresa (`<modulo>.leer` / `<modulo>.gestionar`).

| Recurso | Filtros | Reglas (mismos mensajes que el modo local) |
|---|---|---|
| `/colegios` | `texto` (razón social, NIT), `estado` | NIT único por empresa sin contar el dígito de verificación (409); cursos por grado con salida posterior a la entrada; no se elimina con estudiantes o rutas. |
| `/vehiculos` | `texto` (placa, interno, marca, línea), `tipo`, `estado` | Placa (`ABC123`, se normaliza) y número interno únicos (409); los cuatro vencimientos son filas de `documentos_vehiculo`; no se elimina con asignaciones. |
| `/empleados` | `texto` (nombre, documento), `tipoEmpleado`, `estado` | Documento único; licencia obligatoria para conductores; contrato adjunto; credenciales; no se elimina con asignaciones. |
| `/acudientes` | `texto`, `estado` | Documento único; credenciales del portal; no se elimina con estudiantes a cargo o facturas. |
| `/estudiantes` | `texto`, `acudienteId`, `colegioId`, `grado`, `estado` | Documento único; colegio y acudiente de la empresa (404); el grado debe ser un curso activo del colegio (422); `acudienteId` es el principal de `estudiantes_acudientes`; no se elimina si está en una ruta o tiene facturas. |
| `GET /conductores` | `texto`, `licencia` (VIGENTE, POR_VENCER, VENCIDA), `asignacion` (CON, SIN), `estado` | Solo lectura: empleados conductores + estado de la licencia (30 días) + asignación activa. |

## Operación (fase 4)

| Recurso | Filtros | Reglas |
|---|---|---|
| `/rutas` | `texto` (código, nombre, sector), `colegioId`, `jornada`, `estado`, `estudianteId` | Código único sin distinguir mayúsculas (409); `estudianteIds` en orden de recogida (`rutas_estudiantes.orden_recogida`), del colegio de la ruta (422) y sin otra ruta activa de la misma jornada (422); no se elimina con asignaciones. |
| `/asignaciones` | `rutaId`, `vehiculoId`, `empleadoId` (conductor o monitora), `estado` | `fechaFin` y `monitoraId` vacíos = sin fin / sin monitora. Si queda activa: ruta, vehículo y personal activos; tipo de empleado correcto; capacidad; licencia y documentos sin vencer (422); una activa por ruta y nadie (ni el vehículo) en dos rutas de la misma jornada (409). No se elimina con recorridos. |

**Recorridos** (`/recorridos`, tablas `viajes_ruta` / `paradas_viaje` / `eventos`). El empleado sale del token:

| Petición | Permiso | Respuesta |
|---|---|---|
| `GET /recorridos/del-dia?fecha=` | `recorridos.operar` | Asignaciones activas y vigentes del empleado (conductor o monitora) con ruta activa, su compañero y los recorridos de la fecha. |
| `POST /recorridos {asignacionId, trayecto}` | `recorridos.operar` | 201 con el snapshot (ruta, colegio, vehículo, personal y estudiantes activos con su acudiente principal; ENTREGA al revés). 200 con el existente si el compañero ya lo inició. 409 si otro trayecto está EN_CURSO; 422 si la ruta no es suya hoy o no tiene estudiantes activos. |
| `PATCH /recorridos/{id}/paradas {estudianteIds, estado}` | `recorridos.operar` | Transiciones PENDIENTE → A_BORDO/AUSENTE, A_BORDO → ENTREGADO/PENDIENTE, ENTREGADO → A_BORDO, AUSENTE → PENDIENTE, con hora (422 si no se permite o ya finalizó). |
| `PUT /recorridos/{id}/orden {estudianteIds}` | `recorridos.operar` | Solo la monitora (o el conductor sin monitora, 403); la lista debe ser EXACTAMENTE las paradas por visitar (409). Guarda `ordenActualizado`. |
| `POST /recorridos/{id}/finalizar {observaciones}` | `recorridos.operar` | 422 si queda alguien a bordo o pendiente. |
| `GET /recorridos/{id}` | `recorridos.leer` o `.operar` | El administrador ve cualquiera de su empresa; el empleado solo los suyos (403). |
| `GET /recorridos?texto&desde&hasta&rutaId&trayecto&estado&novedad` | `recorridos.leer` | Historial paginado, del más reciente al más antiguo; `novedad` = AUSENCIAS u OBSERVACIONES. |

Cada cambio actualiza `fechaActualizacion` (el compañero sincroniza comparándola) y registra un evento
(INICIO, CAMBIO_PARADA, REORDEN, FINALIZACION). El índice único (asignación, fecha, trayecto) evita dos
recorridos iguales si ambos inician a la vez: el segundo recibe el existente. Solo puede haber uno EN_CURSO
por asignación **y fecha** (migración `Fase5EnCursoPorFecha`): uno que quedó abierto otro día no bloquea el de hoy.

## GPS y tiempo real (fase 5)

| Petición | Quién | Respuesta |
|---|---|---|
| `POST /recorridos/{id}/gps {posiciones:[{latitud, longitud, velocidad?, rumbo?, fecha}]}` | Conductor o monitora del recorrido (`gps.reportar`) | `{recibidas, descartadas, ultima}`. Máximo 500 por lote; latitud −90…90, longitud −180…180, velocidad 0…300 km/h, rumbo 0…360. Se descartan las repetidas (misma `fecha`) y las que caen fuera de inicio − 5 min … fin + 5 min (un lote sin señal llega bien después de finalizar). `fecha` en ISO UTC. |
| `GET /recorridos/{id}/gps/ultima` | Administrador, personal del recorrido o acudiente de uno de sus estudiantes | Última posición, o 204. El acudiente solo mientras el recorrido está EN_CURSO. |
| `GET /recorridos/{id}/gps?desde=` | Administrador o personal | Trayecto en orden (máx. 5000 puntos); `desde` trae solo lo nuevo. |
| `GET /recorridos/en-curso` | Administrador (`recorridos.leer`) | Recorridos EN_CURSO con conteo de paradas y última posición (mapa). |

**Hub SignalR** `/hubs/recorridos` (protocolo JSON; el token va en `?access_token=` porque el WebSocket del
navegador no envía encabezados, solo se lee en `/hubs` y los logs no registran la query string):
`Seguir(recorridoId)` (mismo acceso que `gps/ultima`), `Dejar(recorridoId)`, `SeguirEmpresa()` (administrador).
Eventos: `posicion` (`PosicionDto`, a quien sigue el recorrido solo mientras está EN_CURSO, y siempre a la empresa)
y `recorridoActualizado {recorridoId, fechaActualizacion, estado}`. Este último llega también SIN `Seguir`: cada
conexión entra al conectarse a su grupo de empleado (`id_empleado`) y de acudiente (`id_acudiente`), y el aviso va
al conductor, la monitora y los acudientes de los estudiantes del recorrido (rutas del día y portal se refrescan
solos; un cliente puede recibirlo dos veces). `Seguir` rechaza al acudiente si el recorrido ya no está en curso.
Los envía `IAvisosRecorrido` (`Api/TiempoReal`), que nunca hace fallar la petición. Con varias instancias del API
hace falta un backplane (ver **Desplegar**).

**Retención:** las lecturas de `gps_vehiculos` con más de `Gps:DiasRetencion` días (30) se borran en cada vuelta
del proceso del servidor (`ProcesoComunicacion`, modo sistema, todas las empresas).

**Parámetros de la empresa** (`ParametrosEmpresa`, scoped, lee `configuracion_sistema` una vez por petición y usa el
valor por defecto si falta o está fuera de rango): `finanzas.dia_limite_pago` (5) para la fecha límite de las cuotas
sin programación (reportes, portal, tablero, notificaciones) y `alertas.dias_vencimiento` (30) para documentos y
licencias por vencer (conductores, tablero, notificaciones).

## Convenciones

- **Rutas:** `/api/v1/<recurso-en-plural-y-en-español>` en minúsculas (`/api/v1/estudiantes`,
  `/api/v1/rutas/{id}/estudiantes`, `/api/v1/recorridos/{id}/paradas`). `GET /health` va fuera de la versión.
- **Contrato del frontend:** cada `RutaRolaApp/src/app/**/<plural>-http.ts` documenta el recurso, los filtros y
  las reglas 409/422. El API los respeta para que el frontend pase a modo HTTP cambiando solo
  `core/repositorios.ts`.
  - CRUD: `GET /x?filtros&pagina&tamano` → `{ items, total, pagina, tamano }`, `GET /x/{id}`, `POST` (201),
    `PUT /x/{id}`, `DELETE /x/{id}` (204), `GET /x/opciones?filtros` → `[{ id, nombre }]` y
    `GET /x/{id}/motivo-no-eliminar` → `{ motivo }` (null si se puede eliminar).
  - Se implementa con `ServicioCrud<TEntidad, TDto, TSolicitud, TFiltros>` (Application/Comun/Crud.cs: filtrar,
    ordenar, mapear, `AplicarAsync` con las reglas, `MotivoNoEliminarAsync`) y `ControladorCrud` en el API. Los
    permisos se declaran con `[PermisosCrud(Permisos.XLeer, Permisos.XGestionar)]` (GET → leer; POST/PUT/DELETE → gestionar).
  - `tamano` acepta números enormes (el frontend pide `Number.MAX_SAFE_INTEGER` para "todo") y se acota a 1000.
- **DTOs siempre:** nunca se devuelve una entidad de EF. Los nombres de los DTOs son los del frontend
  (`acudienteId`, `valorContratacion`, `vencimientoSoat`…), aunque la columna se llame distinto.
- **JSON:** camelCase; enums como texto `UPPER_SNAKE` (`EN_CURSO`, `A_BORDO`); fechas `YYYY-MM-DD`; horas `HH:mm`;
  periodos `YYYY-MM`; booleanos de estado como `"ACTIVO"`/`"INACTIVO"`; ids UUID como texto.
  - Como los formularios del frontend, los textos opcionales vacíos van como `""` (no `null`) en ambos sentidos, y
    una fecha opcional vacía es `""` (`ConvertidorFechaOpcional`).
- **Adjuntos** (contrato del empleado…): `{ nombre, tipo, tamano, url }`. Al guardar, `url` en data URL (base64)
  reemplaza el archivo; otra `url` lo conserva; `null` lo quita. Solo PDF o imagen, máximo 2 MB, en `archivos.contenido`.
  Los listados devuelven `url` vacía; el detalle (`GET /x/{id}`) trae el data URL.
- **Credenciales de empleados y acudientes:** `usuario` + `contrasena` crean o actualizan su fila de `usuarios` (rol
  Conductor/Monitora según `tipoEmpleado`, o Acudiente). El usuario es único en todo el sistema (también frente a
  correos de ingreso) → 409 `El usuario "x" ya está en uso. Elige otro.`; la contraseña es obligatoria al crear,
  vacía al editar = se conserva, nunca se devuelve y cambiarla cierra las sesiones abiertas. Eliminar a la persona
  libera el usuario.
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
- **Auditoría:** `RutaRolaDbContext` registra en `auditoria` cada creación, cambio y eliminación de estudiantes
  (y su vínculo con acudientes), acudientes, empleados, usuarios, pagos y facturas: empresa, usuario, IP, valores
  anteriores y nuevos (solo los campos que cambiaron) en jsonb. Nunca guarda el hash de la contraseña (solo
  `"contrasena": "(cambiada)"`) ni el contenido de archivos; los cambios que solo tocan `ultimo_login` no se registran.

## Finanzas (fase 6)

| Recurso | Filtros | Reglas |
|---|---|---|
| `/facturas` (tabla `facturas` + `facturas_detalle`) | `texto`, `acudienteId`, `estudianteId`, `estado` | `numero` FAC-0001 del servidor. `valorContratacion` = valor **mensual** entre `periodoInicio` y `periodoFin` (`YYYY-MM`); el detalle tiene una línea por mes y `valorTotal`. El estudiante debe ser del acudiente (422); sin facturas vigentes que se crucen para el mismo estudiante (409). Snapshot de nombres. No se elimina con recibos o programación (anularla = INACTIVO). |
| `/recibos` (tabla `pagos`) | `texto`, `facturaId`, `medioPago`, `estado`, `desde`, `hasta` | `numero` REC-0001. Factura vigente y mes dentro de su periodo (422); un recibo vigente por factura + mes (409); fecha de pago no futura. Snapshot de la factura. Anular = INACTIVO. |
| `/cotizaciones` (+ `cotizaciones_estudiantes`) | `texto`, `estado`, `desde`, `hasta` | `numero` COT-0001; `valorTotal = valorPorEstudiante × estudiantes`. Registrados: nombres y teléfono salen del acudiente y estudiantes de la empresa (404 si no existen). Estados PENDIENTE/APROBADA/RECHAZADA. |
| `/programaciones` (+ `plantillas_mensaje`, `plantillas_mensaje_archivos`) | `texto`, `facturaId`, `estado` | Una por factura (409); activa = al menos un canal; por canal habilitado exactamente una plantilla EN_USO (422). Las plantillas se sincronizan por `id` (sin id = nueva); adjuntos PDF/imagen ≤ 2 MB. `GET /{id}/envios` y `GET /envios/conteo?ids=` leen `envios_programacion` (los crea el proceso de cobro, fase 7). |

| Petición | Permiso | Respuesta |
|---|---|---|
| `GET /reportes/ingresos?desde&hasta&medioPago` | `reportes.leer` | Recibos vigentes por mes de pago y por medio, con total, cantidad y promedio. |
| `GET /reportes/facturas-pendientes?texto&acudienteId&situacion` | `reportes.leer` | Cuotas (meses hasta el actual de facturas vigentes sin recibo vigente) con fecha límite (día de la programación o el 5) y `diasMora`; `situacion` VENCIDAS/POR_VENCER. |
| `GET /reportes/morosos?texto&diasMinimos` | `reportes.leer` | Acudientes con cuotas vencidas: estudiantes, facturas, valor adeudado y mora máxima. |
| `GET /tablero` | `tablero.leer` | Indicadores, "Requiere atención", agenda de 30 días, últimos pagos y gráficas, con las reglas de `calcular-tablero.ts`. |

El índice `ix_plantillas_mensaje_en_uso` (programación, canal, estado) **no es único**: al cambiar cuál plantilla
está EN USO, EF actualiza las filas una por una y un índice único fallaría a mitad del cambio; la regla la valida el
servicio. Los consecutivos se calculan con el mayor número existente: dos altas simultáneas chocan con el índice
único y la segunda recibe 409 (reintentar). **Gastos** aún no tiene contrato en el frontend, así que no tiene endpoint.

## Comunicación (fase 7)

| Petición | Permiso | Respuesta |
|---|---|---|
| `GET /notificaciones?bandeja&texto&categoria&nivel&lectura&pagina&tamano` | `notificaciones.propias` | Las del usuario del token, de la más reciente a la más antigua. |
| `GET /notificaciones/resumen` | `notificaciones.propias` | `{noLeidas, criticas, recientes}` (6). Antes revisa si hay hechos nuevos en la empresa. |
| `PATCH /notificaciones/lectura {ids, leida}`, `PATCH /notificaciones/archivo {ids, archivada}`, `POST /notificaciones/leer-todas`, `POST /notificaciones/eliminar {ids}`, `DELETE /notificaciones?bandeja=` | `notificaciones.propias` | 204. Ids ajenos o inexistentes se ignoran; archivar también marca leída; eliminar y vaciar son lógicos (`eliminada`). |
| `GET /portal` | `portal.consultar` (acudiente) | Sus estudiantes (colegio, horario, ruta activa con asignación vigente, recorrido de hoy), estado de cuenta, recibos de sus facturas y sus cotizaciones. 403 para otros roles. |
| `GET /mensajes-whatsapp?texto&acudienteId&estado&desde&hasta` | `mensajes.leer` | Registro paginado de los WhatsApp enviados por el servidor. |

**Notificaciones.** `GeneradorNotificaciones` aplica las reglas de `generar-notificaciones.ts` (eventos de los
últimos 30 días y condiciones del día) para los administradores activos de la empresa. Cada hecho tiene una
`clave` única por usuario (`ix` usuario + clave): se notifica una sola vez aunque se elimine. Se genera al pedir
el resumen si hubo una escritura en la empresa desde la última revisión o pasó un minuto
(`RevisionNotificaciones`, por instancia), y en el proceso periódico.

**Proceso periódico** (`Api/Procesos/ProcesoComunicacion`, cada `Procesos:IntervaloMinutos`): para cada empresa
activa, en su propio scope con solo su `id_empresa`, envía los recordatorios de cobro y genera las notificaciones.
Recordatorio (`ServicioCobros`): programación activa de una factura vigente que cubre el mes en curso, cuota sin
recibo, hoy entre `fecha límite − días previos` y la fecha límite; un envío por canal habilitado y mes
(índice único `ix_envios_programacion_unico`, migración `Fase7Comunicacion`), con la plantilla EN USO y sus
variables (`{acudiente}`, `{estudiante}`, `{factura}`, `{mes}`, `{valor}`, `{fecha_limite}`). Sin teléfono o correo,
o si el proveedor falla, queda FALLIDO (no se reintenta ese mes; la campana lo avisa). Los WhatsApp quedan en
`mensajes_whatsapp`. Los logs nunca incluyen el texto ni el destinatario.

**Pendiente:** el proveedor real de WhatsApp (WhatsApp Business) y de correo: se conectan implementando
`ICanalWhatsapp` / `ICanalCorreo` en `Infrastructure/Mensajeria`. Tampoco hay aún avisos al acudiente de los
eventos del recorrido (recogido, entregado, no asistió).

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
3. ✅ Catálogos: colegios (+cursos), vehículos (+documentos), empleados (+credenciales y contrato), vista de
   conductores, acudientes y estudiantes; auditoría; datos demo; el frontend usa estos módulos y el login por HTTP.
4. ✅ Operación: rutas, asignaciones (fecha fin opcional), recorridos de la Ruta del día (inicio/unión, paradas, orden,
   finalizar, eventos) e historial; el frontend usa estos módulos por HTTP.
5. ✅ GPS: lotes de posiciones, última posición, trayecto, mapa de recorridos en curso y hub SignalR
   (`posicion`, `recorridoActualizado`); un recorrido abierto de otro día ya no bloquea el de hoy.
6. ✅ Finanzas: facturas (+detalle mensual), recibos, cotizaciones, programaciones de cobro (+plantillas y adjuntos,
   historial de envíos), reportes (ingresos, pendientes, morosos) y tablero; datos demo por sección; el frontend usa
   estos módulos por HTTP. Gastos queda pendiente (sin pantalla en el frontend).
7. ✅ Comunicación: notificaciones generadas por el servidor (bandeja por usuario), proceso periódico de
   recordatorios de cobro con registro de WhatsApp (canales simulados hasta conectar proveedor) y portal del
   acudiente; datos demo con fechas históricas; el frontend usa notificaciones y portal por HTTP.
8. ✅ Integración final: GPS desde la Ruta del día y mapas (administrador, historial, acudiente) sobre el hub,
   avisos por grupos de empleado/acudiente, retención GPS, parámetros de la empresa aplicados, cambio de
   contraseña, plataforma de empresas en el frontend, URL de conexión del proveedor y blueprint de Render.
