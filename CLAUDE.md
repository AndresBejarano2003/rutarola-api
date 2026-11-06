# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

RutaRola is a school-transport management system (students, guardians, employees, finance, routes/vehicles). The UI and domain vocabulary are in Spanish — keep new routes, labels, and file names in Spanish to match.

- `RutaRolaApp/` — Angular 21 frontend with its **own git repo** (`RutaRolaApp/.git`, GitHub → Netlify, https://rutarola.netlify.app). The root `RutaRola/` repo versions `api/`, `docs/` and docs, and ignores `RutaRolaApp/`. Never commit (in either repo) until the user asks.
- `api/` — ASP.NET Core backend (.NET 10 + PostgreSQL). See **Backend** below and `api/README.md`.
- `db/` — unused: the schema lives in the EF Core migrations.
- `docs/`, `assets/` — design docs (Apple Pages files), logos, UI images.
- `.github/agents/rutarola-full-stack-maintainer.agent.md` — agent instructions; key points are folded into this file.

## Commands (run from `RutaRolaApp/`)

```bash
npm start              # ng serve, dev config, http://localhost:4200
npm run build          # production build -> dist/RutaRolaApp
npm run watch          # dev build in watch mode
npm test               # Vitest via @angular/build:unit-test (jsdom)
npx ng test --include src/app/core/whatsapp.spec.ts   # single spec file
```

Verify changes with `npm test` and `npm run build`. The production build enforces budgets (initial bundle 500kB warn / 1MB error; per-component styles 4kB warn / 8kB error).

Prettier is installed; there is no lint target configured.

## Frontend architecture

Follows the official Angular style guide (v20+): **organized by feature**, and file/class names **without** the `.component` / `Component` suffix (`estudiantes-page.ts` → `export default class EstudiantesPage`).

```
src/app/
  app.ts, app.config.ts, app.routes.ts, modulos.routes.ts (internal modules + repository providers, lazy)
  core/           global services: auth.ts (AuthService, session + roles), guardias.ts (role guards), whatsapp.ts (WhatsappService), repositorios.ts (data source per module)
  layout/         shell/ (admin, DaisyUI drawer), shell-conductor/ (driver app, no sidebar), shell-portal/ (acudiente portal), barra-superior/ (breadcrumb + user menu), campana-notificaciones/ (bell), menu-lateral/ (opciones-menu.ts)
  shared/         crud/, modal/, campo/, selector-busqueda/, paginacion/, fechas/, exportar/, pruebas/, en-construccion/ …
  autenticacion/  login (+ AutenticacionRepositorio local/http), registro, recuperar, bienvenida
  ruta-del-dia/   driver/monitora app: today's routes + step-by-step recorrido
  portal/         acudiente portal (read-only): their students, facturas, pagos, cotizaciones
  inicio/         dashboard, perfil, configuracion, editar-usuario, notificaciones + inicio.routes.ts
  personas/  finanzas/  operacion/  sistema/      one folder per page + <modulo>.routes.ts
```

- **Standalone components only**, no NgModules (`standalone: true` is the default — don't write it). App bootstraps via `app.config.ts` with `provideRouter(routes, withHashLocation())` (URLs look like `/#/personas/estudiantes`) and `provideHttpClient(withFetch())`.
- **Routing**: `app.routes.ts` has the public pages and `{ path: '', loadChildren: () => import('./modulos.routes') }`. `modulos.routes.ts` is a parent route with `providers: proveerRepositorios()` whose children are `login` (here because local login reads empleados; `canActivate: [soloInvitados]`), the admin modules (`canActivate: [soloRoles('ADMINISTRADOR')]`, `layout/shell/shell`) and `ruta-del-dia` (`soloRoles('CONDUCTOR','MONITORA')`, `layout/shell-conductor`); each loads and its pages via `loadChildren: () => import('./<modulo>/<modulo>.routes')`; each `*.routes.ts` does `export default [...] satisfies Routes`. Pages are `export default class` for `loadComponent`. Every page route carries `data.breadcrumb: string[]` (read by `BarraSuperior` from the deepest active route). Unknown top-level paths redirect to `login`.
- **Unfinished sections** route to `shared/en-construccion/en-construccion-page` (`enConstruccion` constant in the module's routes file). Personas, Operación (colegios, rutas, vehículos, conductores, asignaciones) and Finanzas (cotizaciones, facturas, recibos, programación, reportes) are all functional in local mode; `finanzas/contrataciones` redirects to `facturas`.
- **Adding a page**: create `<modulo>/<nombre>/<nombre>-page.{ts,html}`, add it to `<modulo>.routes.ts`, and add the entry to `layout/menu-lateral/opciones-menu.ts`.
- **Conventions**: `inject()` instead of constructor injection; `input()`/`output()`/`viewChild()` signal APIs instead of decorators; template-only members are `protected`, injected deps `private readonly`; no `any`; use `toSignal`/`takeUntilDestroyed` for observables instead of bare `subscribe`. All selectors use the `app-` prefix. Identifiers and file names in Spanish.
- **State**: Angular signals in `providedIn: 'root'` services.
- **Path alias**: `@environments/*` → `src/environments/*`; `src/app/...` absolute imports also work via `baseUrl`. `environment.apiUrl` is the backend base URL.

### CRUD screen pattern (reference implementation: `personas/estudiantes/`)

Every CRUD screen follows the same layers so it can be wired to the API by changing **one provider**:

| File | Role |
|---|---|
| `<entidad>.ts` | Model (`extends Entidad`), filters interface + empty filters, helpers |
| `<plural>-repositorio.ts` | `abstract class XRepositorio extends RepositorioCrud<X, FiltrosX> {}` — the DI token |
| `<plural>-local.ts` | `extends RepositorioLocal` — TEMPORARY localStorage storage with seed data, filtering, business validation (`ErrorCrud`) |
| `<plural>-http.ts` | `extends RepositorioHttp` — REST against `{apiUrl}/{recurso}` (see table in `shared/crud/repositorio-http.ts`) |
| `<plural>-store.ts` | `extends CrudStore` — signals state (items, total, cargando, guardando, error, filtros, paginación) + toasts. Read-only lists: `extends ListadoStore` |
| `<plural>-filtros/`, `<plural>-tabla/`, `<entidad>-formulario/` | Presentational components (inputs/outputs only) |
| `<plural>-page.ts` | `extends PaginaCrud<T, F>` (`shared/crud/pagina-crud.ts`: modal/modo/seleccionado, `nuevo`, `abrir`, `guardar`, `pedirEliminar` with pre-check, `confirmarEliminar`); declares only `store`, `titulos`, `describir()`; `providers: [XStore]`; uses `app-encabezado-listado`, `app-modal`, `app-acciones-formulario`, `app-dialogo-confirmacion`, `app-paginacion` |

The implementation is chosen in **`core/repositorios.ts`** (`proveerRepositorios()`): `{ provide: XRepositorio, useClass: XLocal }` → switch to `XHttp` when the endpoint exists. `RepositorioLocal` hooks mirror what the API must enforce: `filtrosVacios`, `validar` (sync), `validarRelaciones` (async, cross-module, e.g. asignaciones check capacity/license/expired documents), `preparar` (server-generated consecutives `FAC-/REC-/COT-0001` via `siguienteConsecutivo`), `motivoNoEliminar`. Simulated latency comes from the `LATENCIA_LOCAL` token (300 ms; 0 in tests). Each `*Http` documents its endpoints and 409/422 rules in its doc comment. Extra read-only endpoints: `GET /conductores`, `GET /programaciones/{id}/envios`, `GET /programaciones/envios/conteo?ids=`, `GET /reportes/{ingresos|facturas-pendientes|morosos}`. Shared building blocks live in `shared/crud`, `shared/modal`, `shared/dialogo-confirmacion`, `shared/paginacion` (+ `PaginacionLocal` for client-side paging of reports), `shared/notificaciones` (toasts, rendered in the shell), `shared/formularios` (validators `nit`/`placa`/`posteriorA`, `mensajeError`, `normalizar`, `emitirEnVivo` for live filters), `shared/fechas` (`YYYY-MM` period helpers), `shared/exportar/csv.ts` (`descargarCsv`), `shared/campo` (`[app-campo]` label/error wrapper, `app-titulo-seccion`), `shared/encabezado-listado`, `shared/acciones-fila`, `shared/estado-vacio`, `shared/vencimiento` (`app-badge-vencimiento`, 30-day alert), `shared/lista-seleccion` (multi-select CVA) and `shared/catalogos` (fixed select options only: tipos de documento, `GRADOS` Jardín/Transición/1°…11°, jornadas).

Cross-entity rules:
- **One registry for all repositories.** Modules link to each other (estudiantes ↔ colegios ↔ rutas ↔ asignaciones ↔ vehículos/empleados; acudientes ↔ facturas ↔ recibos ↔ programación ↔ reportes), so every repository is provided once in `core/repositorios.ts`, applied on the parent route of `modulos.routes.ts` (shared instances, lazy, outside the initial bundle). Never declare repository providers in a page or module routes file. When two local repos need each other (empleados ↔ asignaciones), resolve lazily with `Injector.get()` inside the method.
- **Lists of another entity's records.** Every repository exposes `opciones(filtros?: Partial<F>): Observable<OpcionCatalogo[]>` (local: `etiquetaOpcion()`; HTTP: `GET /{recurso}/opciones?<filtros>`). Such lists come from there, never from hard-coded catalogs. Example: `EstudiantesPage` reads `AcudientesRepositorio.opciones()` via `toSignal` + `aMapa`; facturas load `EstudiantesRepositorio.opciones({ acudienteId })`.
- **Referential integrity.** A record in use cannot be deleted.
  - Each repo implements `motivoNoEliminar(id): Observable<string | null>`; `RepositorioLocal.eliminar` throws `ErrorCrud(…, 'EN_USO')`; the API must answer 409 with `{mensaje}`.
  - The page pre-checks and shows `<app-dialogo-confirmacion [permitirConfirmar]="false">` (only "Entendido"), suggesting Inactivo instead.
- **Status field.** Every entity has `estado: EstadoRegistro` (`'ACTIVO'|'INACTIVO'`, `ESTADOS_REGISTRO` in `shared/crud/crud.ts`), rendered with `<app-badge-estado>` (`textoActivo`/`textoInactivo` for Vigente/Anulada etc.). Exception: cotizaciones use PENDIENTE/APROBADA/RECHAZADA.
- **Financial snapshots.** Factura, recibo and cotización store the acudiente/estudiante names and factura number as issued (they don't change if the person is edited later).
- **Module rules** (detailed in README "Reglas de cada módulo"): colegio NIT unique and "Agregar cursos" fills from Jardín up to the chosen grade with the given hours; student grade options come from the colegio's active courses; a student can't be in two active routes of the same jornada; one active asignación per ruta (conductor type + valid license, active vehicle with capacity and documents in force, no vehicle/person in two routes of one jornada); conductores is a read-only view over empleados; facturas = **monthly** value between two months, student must belong to the acudiente, no overlapping active facturas per student; one valid recibo per factura+month; one programación per factura with exactly one `EN_USO` template per enabled channel (sending is done by the server); reports compute from recibos/facturas/programación (default due day 5); Centro de Ayuda (`sistema/soporte`): manuals, tutorials and FAQ live only in `contenido-ayuda.ts` (FAQ answers must match the current routes/rules — update them when a module changes), a solicitud de soporte needs tipo + módulo (options from the menu via `modulosDelMenu()`) + prioridad (default Media) + asunto 5–120 + descripción 20–2000, up to 3 attachments (PDF/image, 1 MB each), and gets a server-generated `SOP-0001` consecutive (local mode: `SolicitudesSoporteService`).
- **People.** `personas/compartido/persona.ts` defines `DatosPersona` (tipo/número de documento + 2 nombres + 2 apellidos) and these helpers:
  - `controlesPersona(fb)`, `limpiarPersona`;
  - `nombreCompleto`, `iniciales`, `coincideTexto`, `compararPorApellido`, `validarDocumentoUnico`.
  - `<app-campos-persona [tiposDocumento]>` renders the Identificación + Datos personales sections inside the parent `<form>` (ControlContainer viewProviders).
  - `<app-celda-persona>` (`shared/celda-persona`) is the first table column.
- **Attachments.** `<app-archivo-adjunto>` (`shared/archivo-adjunto`, CVA, value `ArchivoAdjunto | null`) validates type/size and supports view, replace and remove.
  - Multiple files: `<app-archivos-adjuntos [maximo]>` (value `ArchivoAdjunto[]`).
  - Locally the file is stored as a data URL, so keep the limit at 2 MB; quota errors surface as `SIN_ESPACIO`.
  - The `*Http` sends it as base64 JSON unless `crear`/`actualizar` are overridden for multipart.
- **Inicio / tablero** (`inicio/dashboard/`, route `/inicio`). Real data only — no hard-coded numbers. `TableroRepositorio` (`TableroLocal` → `TableroHttp`, `GET /tablero`) returns a `Tablero` computed by the pure `calcular-tablero.ts` (the API must apply the same rules), reloaded on `CambiosDatos`.
  - Indicators (whole-card links): recaudo del mes (recibos vigentes of the current month vs. monthly value of vigente facturas covering it, with the previous month as reference — no "-100%" at the start of a month), cartera vencida (overdue cuotas, acudientes en mora, what is still within the due date), estudiantes activos (colegios, new this month, without route), operación (active routes, vehicles, drivers, % of seats used).
  - "Requiere atención" replaces static alerts: actionable rows (only count > 0, ordered CRITICA → ADVERTENCIA → INFORMATIVA) linking to the screen that solves them — mora, vehicles with expired documents, expired licenses, failed reminders (30 days), students without route, routes without asignación, documents/licenses expiring within 30 days, contracts ending this or next month, vigente facturas without active programación, pending cotizaciones. Empty → "Todo en orden".
  - "Próximos 30 días" (agenda: document/license expirations, payment due dates grouped by day, contract ends) and "Últimos pagos" (5 latest recibos) replace "Actividad reciente" (the bell covers the activity feed).
  - Charts: facturado vs recaudado (6/12 months), estudiantes por colegio (top 5 + Otros donut), route occupancy as progress bars (≥ 90 % warning; routes without vehicle flagged). ApexCharts colors come from the theme hex values (`COLORES` in `dashboard-page.ts`, labels `#d1d5db`, grid `#374151`).
  - Quick actions use `?accion=nuevo`: `PaginaCrud.ngOnInit` opens the create form when that query param is present. The greeting uses the session user's first name.
- **User types / access** (`core/auth.ts`, `core/guardias.ts`, `autenticacion/`). The server decides the role at login; each role gets ITS system: `ADMINISTRADOR` → `/inicio` (admin shell, all modules); `CONDUCTOR`/`MONITORA` → `/ruta-del-dia` (`shell-conductor`, no sidebar, no admin modules); `ACUDIENTE` → `/portal` (`shell-portal`, `Sesion.acudienteId`) — `rutaInicio(rol)`.
  - `AutenticacionRepositorio` (`AutenticacionLocal` → `AutenticacionHttp`, `POST /auth/login` → `Sesion {usuario, nombre, correo, rol, empleadoId, imagen, inicio}`) validates; `AuthService` (root) stores the session in `rutarola.sesion` (`localStorage` with "Recordarme", else `sessionStorage`). Logout clears ONLY the session, never module data.
  - The login asks for **"Usuario"** (not email); each person picks email, document or any text.
  - Employee AND acudiente credentials are set in their Personas forms, section "Acceso a la app" (`<app-campos-acceso>` + `controlesAcceso`/`contrasenasIguales`/`ajustarContrasena` from `personas/compartido/acceso.ts`): `usuario` (required, unique, 4–40 chars without spaces, stored lowercase, `admin` reserved) and `contrasena` (required on create, min 6, with "Confirmar contraseña"; on edit empty = keep). The password is WRITE-ONLY: the API never returns it and must hash it. The usuario is unique across empleados, acudientes and admin (409); each local repo checks the other module in `validarRelaciones` (`validarUsuarioLibre`); deleting the record frees it.
  - Local login: usuario + contraseña of an empleado → its tipoEmpleado as role; of an acudiente → ACUDIENTE; inactive → rejected; `admin`/`rutarola` (`ADMIN_LOCAL`) → ADMINISTRADOR; generic error "Usuario o contraseña incorrectos". Local passwords live apart from the records in `rutarola.credenciales` (`autenticacion/credenciales-local.ts`, `{usuario: {contrasena, titular: EMPLEADO|ACUDIENTE}}`); seed employees and acudientes use initial + surname (`jcardenas`, `ycharry`, `mperez`…) with `rutarola`. Local mode shows one demo access per role on the login page.
  - Guards: `soloRoles(...)` (no session → `/login?volver=<url>`; other role → its own home) on every module route, `soloInvitados` on login; `volver` only accepts internal paths (`rutaInternaSegura`). Top bar and greeting read the session.
- **Ruta del día** (`ruta-del-dia/`, phone-first). `RecorridosRepositorio` (`RecorridosLocal`, `rutarola.recorridos` → `RecorridosHttp`, endpoints + 409/422 in its doc comment); the employee comes from the session/token, never a parameter.
  - Today's routes = the employee's ACTIVE asignaciones valid on the date (`fechaInicio ≤ hoy ≤ fechaFin`) with an active ruta; shows colegio, vehicle, student count and the partner (conductor ↔ monitora) with a call link. Conductor and monitora share the same recorrido.
  - Trayectos: `RECOGIDA` (home → school, route order) and `ENTREGA` (school → home, reverse order). One recorrido per asignación + date + trayecto; only one `EN_CURSO` per asignación. `iniciar` on a trayecto the partner already started returns THAT recorrido (join, info toast — not an error; HTTP 200 instead of 201) and "Mis rutas" refreshes itself via `observarRutasDelDia` (shows "Continuar"). Starting snapshots ruta/colegio/vehicle/staff and, per active student, name, grade, address and acudiente + phone.
  - Per student: `PENDIENTE → A_BORDO → ENTREGADO` or `PENDIENTE → AUSENTE`, with times; "Deshacer" goes back one step; other transitions are rejected. `siguientePaso` drives the UI (recogida: next pickup, then "Entregar en el colegio" for everyone on board; entrega: boarding at school, then each home), with Google Maps, call and WhatsApp links.
  - Admin history: Operación › Recorridos (`operacion/recorridos/`, read-only, `HistorialRecorridosRepositorio` → `GET /recorridos`): newest first, filters texto/date range/ruta/trayecto/estado/novedad (absences or observations), detail with per-student times, CSV export of everything filtered.
  - Visit order: the MONITORA (or the conductor when the asignación has no monitora — `puedeReordenar`) drags a remaining student (⠿ handle with pointer events, plus ↑/↓ buttons and arrow keys; no CDK) to a new position: 2 → 6 shifts 3–6 up one. Only stops still to visit are reordered (RECOGIDA: PENDIENTE; ENTREGA: PENDIENTE + A_BORDO); visited ones stay on top (`paradasEnOrden`). The list sent must be EXACTLY the remaining ones (`reordenarParadas`, else 409 + reload). Stores `ordenActualizado {empleadoId, nombre, hora}`. `PUT /recorridos/{id}/orden {estudianteIds}`.
  - Conductor ↔ monitora sync: `RecorridosRepositorio.observar(id)` (local: `storage` event from another tab + every 10 s; HTTP: polling every `INTERVALO_SINCRONIZACION_MS` until SSE/WebSocket). When the partner changed the order, the page shows "… cambió el orden · Ahora sigue: …" (vibrates) and the next step updates. Updates are ignored while the user is dragging or saving.
  - Finalize only when everyone is ENTREGADO or AUSENTE — **no student may remain on board**; a finished recorrido is read-only. Big buttons, one `btn-primary`, no toast per mark (only errors and finishing).
- **Portal del acudiente** (`portal/`, phone-first, READ-ONLY). The acudiente comes from the session/token, never a parameter; they see only their own students, facturas, recibos and cotizaciones. `PortalRepositorio` (`PortalLocal` → `PortalHttp`, `GET /portal` → `PortalAcudiente`), shared state in `PortalEstado` (provided in `portal.routes.ts`). Sections (tabs from `sm`, fixed bottom nav on phones): Inicio (saldo vencido, próximo pago, pagado este año, "Hoy en la ruta", last payments), Estudiantes (colegio, horario, pickup address, ruta, vehicle, conductor/monitora with call link, today's status via `situacionRecorrido`), Pagos, Cotizaciones.
  - Estado de cuenta = pure `calcularEstadoCuenta` (the API must apply the same rules): each month of a vigente factura is `PAGADA` (vigente recibo), `VENCIDA` (past the due date without recibo), `PENDIENTE` (current month within the due date) or `PROGRAMADA` (future). Due date = `fechaLimiteCuota` (`finanzas/reportes/reportes.ts`: programación's día límite or day 5). Anuladas generate no cuotas; anulado recibos don't count. Próximo pago: VENCIDA > PENDIENTE > PROGRAMADA, then by date.
- **Notifications** (`inicio/notificaciones/`, bell in `layout/campana-notificaciones`, page `/inicio/notificaciones`). Different from toasts (`shared/notificaciones` = ephemeral feedback).
  - The SERVER creates them; the user only reads, marks read/unread, archives/restores, deletes (one, a selection, or "Vaciar" a whole bandeja with confirmation). Archiving also marks read. Single delete is immediate; bulk delete and vaciar need `<app-dialogo-confirmacion>`.
  - Rules live in `generar-notificaciones.ts` (pure; the API must apply the same ones): events within `VENTANA_EVENTOS_DIAS` (30) — pago recibido, nueva factura/cotización, cotización aprobada/rechazada, vehículo/empleado registrado, envío realizado/fallido; conditions checked daily — vehicle documents and conductor licenses por vencer (`DIAS_ALERTA_VENCIMIENTO`, ADVERTENCIA) or vencidos (CRITICA), contrato por terminar (ends this or next month) / terminado with the factura still active, acudiente en mora (CRITICA).
  - Every notification has a `clave` identifying the fact (`licencia:{id}:{fecha}:{estado}`…); a fact is notified ONCE, even if deleted (local: `rutarola.notificaciones.vistas`). New overdue months / renewed dates produce a new clave.
  - Levels CRITICA/ADVERTENCIA/INFORMATIVA/EXITO → error/warning/info/success tokens; icon by category (`CATEGORIAS_NOTIFICACION`). Each one links to the related screen (`enlace`).
  - `CentroNotificaciones` (provided in `proveerRepositorios()` next to `NotificacionesRepositorio`) holds the shared state: refreshes on start, every 60 s and whenever `CrudStore` saves/deletes (`shared/crud/cambios-datos.ts` → `CambiosDatos.notificar()`); the page reloads on its `version`. The bell badge shows unread (red if any critical, `99+`), the 6 most recent, "Marcar todas como leídas" and "Ver todas"; clicking one marks it read and opens it expanded (`?abrir=<id>`). On phones the bell panel is fixed full-width.
- **Locale.** `LOCALE_ID` is `es-CO`. Money uses `currency: 'COP' : 'symbol-narrow' : '1.0-0'`.
- **Tests.** `shared/pruebas/pruebas.ts` provides `repositoriosDePrueba()` (all of `proveerRepositorios()` + `LATENCIA_LOCAL` 0), `simularDialogos()` (jsdom lacks `showModal` and `ResizeObserver`), `estabilizar()` and `pantalla(el)` (escribir / elegir / boton / filas). Tests don't get `LOCALE_ID` es-CO, so don't assert locale-formatted numbers literally. Every CRUD page gets a `*-page.spec.ts` covering list, filter, create with validation and delete; cross-module rules are tested on the repositories (`operacion/reglas-operacion.spec.ts`, `finanzas/reglas-finanzas.spec.ts`); `app/pantallas.spec.ts` smoke-renders the remaining pages.

All components use `ChangeDetectionStrategy.OnPush` (the app is zoneless — no zone.js). Use typed reactive forms (`NonNullableFormBuilder`). `resource`/`rxResource`/`httpResource` and Signal Forms are still `@experimental` in Angular 21.2 — don't build on them yet.

### Placeholders to be aware of

There is no real auth: `AutenticacionLocal` keeps unhashed passwords in the browser (demo only) and a fixed local admin (`admin`, `USUARIO_DEMO` profile); switch to `AutenticacionHttp`, add a token interceptor and an admin-users module when the API exists. Recorridos are stored locally (`RecorridosLocal`); the acudiente portal is computed in the browser (`PortalLocal`) until `GET /portal` exists. `WhatsappService` posts to `${apiUrl}/whatsapp/enviar.php`, which doesn't exist in this repo; automatic billing sends (programación) must be done by the backend. The local admin profile (`USUARIO_DEMO`) and Membresías are hard-coded sample data; the dashboard is computed locally by `TableroLocal`; support requests (`sistema/soporte`) are stored locally until a support API exists; notifications are generated in the browser by `NotificacionesLocal` (switch to `NotificacionesHttp` when the server generates them); Personas, Operación and Finanzas run in local mode (`localStorage`). Templates still open DaisyUI modals with inline `onclick="id.showModal()"`.

## Styling and design rules (mandatory for every screen)

- Tailwind CSS v4 via PostCSS (`.postcssrc.json`) + DaisyUI 5. `src/styles.css` disables the built-in DaisyUI themes (`themes: false`) and defines ONE custom dark theme, **`rutarola`** (`index.html` → `data-theme="rutarola"`).
- **Use the theme's semantic classes, never fixed grays/blues** in new or touched templates:

  | Token | Hex | Use |
  |---|---|---|
  | `bg-base-100` | #111827 | page background, inputs, selects (`bg-base-100 border-neutral`) |
  | `bg-base-200` | #1f2937 | cards, modals, tables (`card bg-base-200 border border-base-300 shadow-xl`) |
  | `bg-base-300` | #374151 | table header, row hover (`hover:bg-base-300/50`), neutral buttons (`bg-base-300 border-neutral hover:bg-neutral`) |
  | `text-base-content` | #f3f4f6 | main text; secondary text `text-base-content/70` (**never below /70 for text**; /50–/60 only for decorative icons) |
  | `primary` | #facc15 | brand bus yellow — the ONE main action per view (`btn-primary`), current page in pagination, edit icon |
  | `secondary` | #2563eb | secondary actions/links |
  | `accent` | #dc2626 | brand red — brand accents only, NOT errors |
  | `info` / `success` / `warning` / `error` | light tones | consult icon, active state, local-mode badge, delete/validation |

- Every text/background pair must meet **WCAG AA (≥ 4.5:1)**. `gray-500` on `gray-800` (3.0:1) is not allowed.
- **Tables**: wrapper `@container overflow-x-auto rounded-box border border-base-300`; header row `bg-base-300 text-base-content/80 text-xs uppercase tracking-wide`; rows `border-base-300 hover:bg-base-300/50 cursor-pointer` (click = consult); status with `badge badge-soft` (`badge-success` active, neutral `bg-base-300` inactive); actions as `btn-ghost btn-square` with tooltips: consult `text-info`, edit `text-primary`, delete `text-error`; skeleton rows while loading; empty state with icon + message (+ create button when there are no filters).
- **Selection lists (filters AND forms, every screen)**: never a plain `<select>` — always `<app-selector-busqueda>` (`shared/selector-busqueda`), a combobox where the user types to search (accent/case-insensitive), with ↑/↓/Enter/Esc, a check on the selected option, "Sin resultados" message, and it opens upward when there's no room below (e.g. bottom of a scrolling modal body). It's a `ControlValueAccessor` (`formControlName`/`ngModel`, disabled state supported). Filters: `textoTodos="Todos los …"` adds the `''` option + clear button. Forms: `[invalido]="!!error('campo')"`, and wrap it in a `<div class="flex flex-col">` + `<span class="label-text">`, NOT a `<label>` (the selector already contains one). Exception: 2–4 fixed options that are clearer seen at once (e.g. tipo/prioridad of a solicitud de soporte) may be radio tiles: `<label>` + `sr-only` radio + `has-checked:border-primary has-checked:bg-primary/10 has-focus-visible:outline-2`.
- **Pagination**: always `<app-paginacion>` (numbered pages with "…", current page `btn-primary`, others neutral, "Mostrando a–b de n", rows-per-page selector).
- **Forms**: grouped in sections (`text-info` uppercase headings), labels `text-base-content/90`, required mark `*` in `text-error`, error message under the field (`mensajeError`), primary submit with spinner while saving.
- **Modals** (`<app-modal>`) have three zones: fixed header, body with its own scroll, **fixed footer** for the buttons — the user never scrolls to find Save. Content with the `pie` attribute goes to the footer. Form components contain ONLY fields (`<form [id]="idFormulario()">`); the buttons are `<app-acciones-formulario pie formulario="form-x" ...>` (Cancel/Close, Edit in consult mode, Save with spinner), which submits via the HTML `form` attribute. Each zone needs its own `@if (modalAbierto())` block with a single root element, otherwise Angular projects it into the default slot. Confirmation dialogs also put their buttons in the footer.
- **Responsive (mandatory).** Every screen must work from 320 px phones (portrait and landscape) through tablets, laptops and 2560 px screens; check 320/375/768/1024/1280/1920/2560.
  - Shell: `h-dvh` with a ☰ button in the top bar below `lg` (menu closes on navigation) and a fixed sidebar from `lg`; content `min-w-0`, max width 1920 px centered; iPhone safe areas (`viewport-fit=cover`, `env(safe-area-inset-*)`).
  - The page never scrolls horizontally; wide tables scroll inside their card.
  - Tables: wrapper `@container overflow-x-auto …`; single-line rows (headers may wrap); last column (actions/value) sticky right; optional columns use **container queries** on the card width, not viewport breakpoints (`hidden @3xl:table-cell` ≥768, `@5xl:` ≥1024, `@6xl:` ≥1152), because the sidebar eats 256 px from `lg`. From a 768 px card the table must fit without scrolling. These rules live in `styles.css` inside `@layer components`, so a cell can opt out with utilities (`whitespace-normal min-w-44`).
  - Filters: wrap the fields in `<app-panel-filtros (limpiar)="…">` (collapsible on phones, always open on `amplio:` = ≥48rem wide and ≥32rem tall, a custom variant in `styles.css`).
  - Grids start at one column on mobile; grid children get `min-width: 0` globally. Modals are bottom sheets on phones. Button groups use `flex flex-wrap`; dropdowns use `max-w-[calc(100vw-1rem)]`.
  - Font Awesome's unlayered CSS beats Tailwind visibility utilities on `<i>`: wrap the icon in a `<span class="md:hidden">`.
- Feedback via toasts (`NotificacionesService`, never `alert()`), destructive actions via `<app-dialogo-confirmacion>`.
- **In-page navigation** never uses `href="#…"` (with `withHashLocation` it changes the route): give the section an `id` + `scroll-mt-4` and call `scrollIntoView`.
- **Quick-access cards** are whole clickable `<button class="card …">` with a colored icon, not cards with colored buttons inside (keeps one `btn-primary` per view).
- **Embedded media**: videos use `aspect-video` (never a fixed height) and `youtube-nocookie.com`; PDF viewers also offer "Abrir en otra pestaña" and "Descargar" because mobile browsers don't render PDFs in an iframe. Both open in `<app-modal>`.
- Screens not yet migrated still use hard-coded `bg-gray-*` classes; migrate them to the tokens when they are touched. `personas/estudiantes/` is the reference.
- Icons: Font Awesome 7 from CDN (`fa-solid ...`). Charts: `ng-apexcharts`. Static files (logos, manuals under `public/documentacion/`) are served from `public/`.

## Backend (`api/`)

ASP.NET Core Web API on **.NET 10**, C# with nullable enabled (null warnings are errors, `Directory.Build.props`), **PostgreSQL** via EF Core + Npgsql (Code First), FluentValidation, ProblemDetails, Serilog (console; compact JSON outside Development), OpenAPI + Scalar at `/docs` (not in Production), xUnit + Testcontainers. Full docs: `api/README.md`, which lists env vars, deploy steps and the differences from `docs/modelo-bd-rutarola.json`.

```
api/src/RutaRola.Api/            Program.cs (+ `migrar` command), Configuracion/ (Servicios: DI, JSON, CORS, health; Observabilidad: Serilog, Scalar), Errores/ (ManejadorErrores, DetallesProblema), Seguridad/ (ContextoUsuarioHttp), Controllers/
api/src/RutaRola.Application/    Comun/ (IContextoUsuario, ErrorNegocio+CodigoError, Paginacion, ListaPaginada, Reclamos = JWT claim names); per module: Dtos, Validadores, Servicios
api/src/RutaRola.Domain/         Comun/ (Entidad, EntidadEmpresa, IEntidadEmpresa, Persona), Enums/, Seguridad/ (Roles, Permisos.PorRol), Personas/, Operacion/, Finanzas/, Comunicacion/
api/src/RutaRola.Infrastructure/ Persistencia/ (RutaRolaDbContext, Configuraciones/<Modulo>.cs, Migraciones/, Seed/SembradorDatos, BaseDatos, FabricaDisenio), DependencyInjection.cs
api/tests/RutaRola.Tests/        Infraestructura/BaseDatosPrueba (Testcontainers Postgres + WebApplicationFactory, collection "BaseDatos")
```

Commands (from `api/`; Docker must be running for tests and compose):

```bash
docker compose up --build            # Postgres + API at http://localhost:8080/docs (needs api/.env, see .env.example)
docker compose up -d db && dotnet run --project src/RutaRola.Api    # http://localhost:5080/docs (user-secrets for the connection string)
dotnet build && dotnet test
dotnet ef migrations add <Nombre> -p src/RutaRola.Infrastructure -s src/RutaRola.Infrastructure -o Persistencia/Migraciones
dotnet run --project src/RutaRola.Api -- migrar     # apply migrations + idempotent seed (explicit deploy step)
```

Conventions:
- **The frontend is the contract.** Each `RutaRolaApp/src/app/**/<plural>-http.ts` documents its resource, filters and 409/422 rules. Match them exactly, so switching a module to HTTP only changes `core/repositorios.ts`.
  - CRUD: `GET /x?filtros&pagina&tamano` → `{items,total,pagina,tamano}` (`ListaPaginada<T>`), `GET/PUT/DELETE /x/{id}`, `POST /x`, `GET /x/opciones` → `[{id,nombre}]`.
  - `environment.apiUrl` = `https://<host>/api/v1`.
- **Routes**: `/api/v1/<plural-en-español>`, lowercase. `GET /health` (checks the DB) is outside the version. Recorridos keep the frontend name (`/recorridos`, `/recorridos/{id}/paradas`, `/recorridos/{id}/gps`); the tables are `viajes_ruta` / `paradas_viaje`.
- **DTOs only**: never return EF entities. DTO field names are the frontend's (`acudienteId`, `valorContratacion`, `vencimientoSoat`, `modelo` = año…).
- **JSON**: camelCase; enums as `UPPER_SNAKE` text (`JsonNamingPolicy.SnakeCaseUpper`: `EnCurso` → `EN_CURSO`, `ABordo` → `A_BORDO`); `DateOnly` `YYYY-MM-DD`; `TimeOnly` `HH:mm` (`ConvertidorHora`); periods `YYYY-MM` (stored as the 1st of the month); boolean `Activo` → `"ACTIVO"/"INACTIVO"`; UUID ids as strings.
- **Errors**: throw `ErrorNegocio(mensaje, CodigoError)` → ProblemDetails with the `mensaje` extension (the frontend shows it), plus `traceId` and `errores` for validation.
  - Status codes: 404 NoEncontrado (also for other companies' records), 409 Duplicado/EnUso/Conflicto, 422 Validacion, 403 Prohibido, 400 for FluentValidation.
  - Postgres unique and FK violations and concurrency errors map to 409 automatically.
- **Database**:
  - Naming and types:
    - snake_case; plural tables; PK `id_<entidad>` (UUID v7 via `Guid.CreateVersion7()`; bigint identity for `gps_vehiculos`, `auditoria`, `eventos`); FKs are `Id<Padre>` properties → `id_<padre>`, configured explicitly in `Configuraciones/`.
    - Money `numeric(14,2)` (`Extensiones.Dinero`), coordinates `numeric(9,6)`, `timestamptz` in UTC. Enums are stored as `UPPER_SNAKE` text (`ConvertidorEnum`, applied to every enum).
  - Deletes: no cascade by default (`CascadeDeleteConvention` removed); add `.OnDelete(Cascade)` only for composition children.
  - Indexes: unique partial indexes need `.HasDatabaseName(...)`, and a second index on the same column needs a name.
  - `Entidad` sets `FechaCreacion`/`FechaActualizacion` on save.
- **Multi-company (critical)**:
  - Entities with `IdEmpresa` implement `IEntidadEmpresa` (inherit `EntidadEmpresa`/`Persona`) and get the named query filter `RutaRolaDbContext.FiltroEmpresa` automatically.
  - Child tables without `id_empresa` get an explicit filter through their parent in `OnModelCreating`. Add one for every new child entity.
  - No company in the token → nothing is visible.
  - `SaveChanges` sets `IdEmpresa` from the token on insert (never from the body) and throws 403 on modifying/deleting another company's row or changing its `IdEmpresa`.
  - `UsarModoSistema()` is only for the seed and server jobs.
  - The filter does NOT stop *referencing* a foreign id: services must load every referenced id (`colegioId`, `acudienteId`…) through the filtered context and return 404 when it is missing.
  - Every new module needs isolation tests (A cannot read or modify B).
- **Roles/permissions**:
  - Roles are SuperAdmin, AdminEmpresa (sent as `ADMINISTRADOR` in the frontend `Sesion`), Conductor, Monitora and Acudiente.
  - Permissions `modulo.accion` live in `Domain/Seguridad/Catalogo.cs` (`Permisos`, `Permisos.PorRol`). The seed syncs `permisos`/`roles_permisos`; each permission is an authorization policy.
  - Login is by `usuario` (unique across the system, lowercase) or email. Passwords use the Identity `PasswordHasher`; refresh tokens are stored hashed.
- **Deletes**: `DELETE` removes physically only when nothing references the row; otherwise 409 with `{mensaje}`. Setting a record to INACTIVO is the logical delete.
- **Secrets**: never in the repo. Use `dotnet user-secrets` (UserSecretsId in `RutaRola.Api.csproj`), `api/.env` (git-ignored) or hosting env vars: `ConnectionStrings__RutaRola`, `Seed__Admin__Password`, `Cors__Origenes` (comma-separated).
- **Migrations**: never applied automatically in Production. Use the `migrar` command. `BaseDatos:MigrarAlIniciar` (Development, compose) is ignored in Production.
- **Minors' data**: request logs omit query strings and bodies; do not log DTOs with student data.

## Documentation rule

Every system-wide base rule (architecture, CRUD pattern, design) must be recorded in **both** this file and `RutaRolaApp/README.md` (Spanish, "Reglas de negocio"). Backend conventions go in this file and `api/README.md`. Keep them consistent.

## Constraints

- Keep TypeScript/Angular strictness (`strict`, `strictTemplates`, `noPropertyAccessFromIndexSignature`, etc.); don't relax compiler settings to make something pass.
- Reuse the installed Angular, Tailwind, DaisyUI, and ApexCharts dependencies; don't add dependencies without a concrete need.
