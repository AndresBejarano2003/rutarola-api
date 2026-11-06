namespace RutaRola.Domain.Seguridad;

/// <summary>Nombres de los roles base (sembrados al migrar).</summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string AdminEmpresa = "AdminEmpresa";
    public const string Conductor = "Conductor";
    public const string Monitora = "Monitora";
    public const string Acudiente = "Acudiente";
}

/// <summary>
/// Permisos (<c>modulo.accion</c>). El seeder los sincroniza con la tabla <c>permisos</c> y con
/// <c>roles_permisos</c> según <see cref="PorRol"/>; cada uno es una policy de autorización.
/// </summary>
public static class Permisos
{
    public const string EmpresasGestionar = "empresas.gestionar";
    public const string UsuariosLeer = "usuarios.leer";
    public const string UsuariosGestionar = "usuarios.gestionar";
    public const string ConfiguracionGestionar = "configuracion.gestionar";
    public const string AuditoriaLeer = "auditoria.leer";

    public const string EstudiantesLeer = "estudiantes.leer";
    public const string EstudiantesGestionar = "estudiantes.gestionar";
    public const string AcudientesLeer = "acudientes.leer";
    public const string AcudientesGestionar = "acudientes.gestionar";
    public const string EmpleadosLeer = "empleados.leer";
    public const string EmpleadosGestionar = "empleados.gestionar";

    public const string ColegiosLeer = "colegios.leer";
    public const string ColegiosGestionar = "colegios.gestionar";
    public const string VehiculosLeer = "vehiculos.leer";
    public const string VehiculosGestionar = "vehiculos.gestionar";
    public const string RutasLeer = "rutas.leer";
    public const string RutasGestionar = "rutas.gestionar";
    public const string AsignacionesLeer = "asignaciones.leer";
    public const string AsignacionesGestionar = "asignaciones.gestionar";
    public const string RecorridosLeer = "recorridos.leer";
    public const string RecorridosOperar = "recorridos.operar";
    public const string GpsReportar = "gps.reportar";
    public const string GpsConsultar = "gps.consultar";

    public const string FacturasLeer = "facturas.leer";
    public const string FacturasGestionar = "facturas.gestionar";
    public const string PagosLeer = "pagos.leer";
    public const string PagosGestionar = "pagos.gestionar";
    public const string CotizacionesLeer = "cotizaciones.leer";
    public const string CotizacionesGestionar = "cotizaciones.gestionar";
    public const string GastosLeer = "gastos.leer";
    public const string GastosGestionar = "gastos.gestionar";
    public const string ProgramacionesLeer = "programaciones.leer";
    public const string ProgramacionesGestionar = "programaciones.gestionar";
    public const string ReportesLeer = "reportes.leer";
    public const string TableroLeer = "tablero.leer";

    public const string PortalConsultar = "portal.consultar";
    public const string NotificacionesPropias = "notificaciones.propias";
    public const string MensajesLeer = "mensajes.leer";
    public const string MensajesGestionar = "mensajes.gestionar";

    public static readonly IReadOnlyDictionary<string, string> Descripciones = new Dictionary<string, string>
    {
        [EmpresasGestionar] = "Crear y administrar empresas de transporte (plataforma)",
        [UsuariosLeer] = "Ver usuarios de la empresa",
        [UsuariosGestionar] = "Crear, editar y desactivar usuarios de la empresa",
        [ConfiguracionGestionar] = "Cambiar la configuración de la empresa",
        [AuditoriaLeer] = "Consultar la auditoría",
        [EstudiantesLeer] = "Ver estudiantes",
        [EstudiantesGestionar] = "Crear, editar y eliminar estudiantes",
        [AcudientesLeer] = "Ver acudientes",
        [AcudientesGestionar] = "Crear, editar y eliminar acudientes",
        [EmpleadosLeer] = "Ver conductores y monitoras",
        [EmpleadosGestionar] = "Crear, editar y eliminar conductores y monitoras",
        [ColegiosLeer] = "Ver colegios",
        [ColegiosGestionar] = "Crear, editar y eliminar colegios",
        [VehiculosLeer] = "Ver vehículos",
        [VehiculosGestionar] = "Crear, editar y eliminar vehículos",
        [RutasLeer] = "Ver rutas",
        [RutasGestionar] = "Crear, editar y eliminar rutas",
        [AsignacionesLeer] = "Ver asignaciones",
        [AsignacionesGestionar] = "Crear, editar y eliminar asignaciones",
        [RecorridosLeer] = "Ver el historial de recorridos",
        [RecorridosOperar] = "Iniciar y registrar los recorridos asignados (ruta del día)",
        [GpsReportar] = "Enviar posiciones GPS del recorrido asignado",
        [GpsConsultar] = "Ver la ubicación de los recorridos",
        [FacturasLeer] = "Ver facturas",
        [FacturasGestionar] = "Crear, editar y anular facturas",
        [PagosLeer] = "Ver recibos de pago",
        [PagosGestionar] = "Registrar y anular recibos de pago",
        [CotizacionesLeer] = "Ver cotizaciones",
        [CotizacionesGestionar] = "Crear y editar cotizaciones",
        [GastosLeer] = "Ver gastos",
        [GastosGestionar] = "Registrar y editar gastos",
        [ProgramacionesLeer] = "Ver programaciones de cobro",
        [ProgramacionesGestionar] = "Crear y editar programaciones de cobro",
        [ReportesLeer] = "Ver reportes financieros",
        [TableroLeer] = "Ver el tablero de inicio",
        [PortalConsultar] = "Consultar el portal del acudiente (solo sus estudiantes)",
        [NotificacionesPropias] = "Gestionar sus propias notificaciones",
        [MensajesLeer] = "Ver el registro de mensajes de WhatsApp",
        [MensajesGestionar] = "Registrar mensajes de WhatsApp",
    };

    public static IEnumerable<string> Todos => Descripciones.Keys;

    private static readonly string[] Operativos =
        [RecorridosOperar, GpsReportar, NotificacionesPropias];

    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> PorRol =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [Roles.SuperAdmin] = [EmpresasGestionar, UsuariosLeer, UsuariosGestionar, AuditoriaLeer, NotificacionesPropias],
            [Roles.AdminEmpresa] = Descripciones.Keys
                .Where(p => p is not EmpresasGestionar and not RecorridosOperar and not GpsReportar and not PortalConsultar)
                .ToArray(),
            [Roles.Conductor] = Operativos,
            [Roles.Monitora] = Operativos,
            [Roles.Acudiente] = [PortalConsultar, GpsConsultar, NotificacionesPropias],
        };
}
