# RutaRola

Sistema de gestión de transporte escolar (multiempresa).

- `RutaRolaApp/` — frontend Angular (repositorio propio en GitHub, desplegado en Netlify:
  https://rutarola.netlify.app). **Las reglas de negocio, estructura y diseño del sistema están en
  [`RutaRolaApp/README.md`](RutaRolaApp/README.md).**
- `api/` — backend ASP.NET Core (.NET 10) + PostgreSQL. Cómo correrlo, variables, migraciones y despliegue:
  [`api/README.md`](api/README.md).
- `docs/` — modelo relacional (`modelo-bd-rutarola.json/.pdf`), documentos de diseño. `assets/` — logos e imágenes.
- `db/` — sin uso: el esquema lo definen las migraciones de EF Core en `api/src/RutaRola.Infrastructure/Persistencia/Migraciones/`.

Este repositorio (raíz) versiona `api/`, `docs/` y la documentación; ignora `RutaRolaApp/`, que tiene su propio repositorio.
