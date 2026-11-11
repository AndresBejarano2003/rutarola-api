using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Suscripciones;

namespace RutaRola.Application.Suscripciones;

public record LimitesDto(int? Vehiculos, int? Estudiantes);

public record UsoDto(int Vehiculos, int Estudiantes);

public record PlanDto(
    PlanSuscripcion Id, string Nombre, string Descripcion, long PrecioMensual, long PrecioAnual, bool PrecioDesde, bool Destacado,
    LimitesDto Limites, IReadOnlyList<string> Funciones, IReadOnlyList<string> Incluye);

/// <summary>
/// Plan de la empresa. <c>periodo</c> <c>""</c> sin pago; <c>fechaFin</c> null en Gratis; <c>diasRestantes</c> de la
/// prueba o del periodo pagado. <c>pagosEnLinea</c> = Wompi configurado.
/// </summary>
public record SuscripcionDto(
    PlanSuscripcion Plan, string NombrePlan, EstadoSuscripcion Estado, string Periodo, DateTime FechaInicio, DateTime? FechaFin,
    int? DiasRestantes, bool PruebaUsada, LimitesDto Limites, UsoDto Uso, IReadOnlyList<string> Funciones, bool PagosEnLinea, string Nota);

/// <summary><c>valor</c> = lo pagado (ya con el descuento); <c>codigoPromocional</c> <c>""</c> sin código.</summary>
public record PagoSuscripcionDto(
    Guid Id, string Referencia, PlanSuscripcion Plan, PeriodoPago Periodo, long Valor, EstadoPagoSuscripcion Estado, string MetodoPago,
    string IdTransaccion, DateTime FechaCreacion, DateTime? FechaAplicacion, string CodigoPromocional, long Descuento);

/// <summary>Datos para abrir el Web Checkout de Wompi (la firma se calcula en el servidor).</summary>
public record CheckoutDto(string Referencia, string Url, long ValorCentavos, string Moneda, string LlavePublica, string Firma, string UrlRetorno);

public record SolicitudPagoSuscripcion(PlanSuscripcion? Plan, PeriodoPago? Periodo, string? CodigoPromocional = null);

public record SolicitudVerificacionPago(string? IdTransaccion);

/// <summary>Ajuste manual de la plataforma (convenio, pago por transferencia, extensión de la prueba…).</summary>
/// <summary><c>codigoPromocional</c> (opcional, solo en ACTIVA): registra el uso y anota el descuento del primer mes.</summary>
public record SolicitudAjusteSuscripcion(
    PlanSuscripcion? Plan, EstadoSuscripcion? Estado, PeriodoPago? Periodo, DateTime? FechaFin, string? Nota, string? CodigoPromocional = null);

public class ValidadorSolicitudPagoSuscripcion : AbstractValidator<SolicitudPagoSuscripcion>
{
    public ValidadorSolicitudPagoSuscripcion()
    {
        RuleFor(x => x.Plan).NotNull().WithMessage("Elige el plan.")
            .NotEqual(PlanSuscripcion.Gratis).WithMessage("El plan Gratis no requiere pago.");
        RuleFor(x => x.Periodo).NotNull().WithMessage("Elige si pagas mensual o anual.");
        RuleFor(x => x.CodigoPromocional).MaximumLength(30).WithMessage("El código promocional no es válido.");
    }
}

public class ValidadorSolicitudVerificacionPago : AbstractValidator<SolicitudVerificacionPago>
{
    public ValidadorSolicitudVerificacionPago()
    {
        RuleFor(x => x.IdTransaccion).MaximumLength(80).WithMessage("La transacción no es válida.");
    }
}

public class ValidadorSolicitudAjusteSuscripcion : AbstractValidator<SolicitudAjusteSuscripcion>
{
    public ValidadorSolicitudAjusteSuscripcion()
    {
        RuleFor(x => x.Plan).NotNull().WithMessage("Elige el plan.");
        RuleFor(x => x.Estado).NotNull().WithMessage("Elige el estado.");
        RuleFor(x => x.FechaFin).NotNull().When(x => x.Estado is EstadoSuscripcion.Prueba or EstadoSuscripcion.Activa)
            .WithMessage("Indica hasta cuándo está vigente.");
        RuleFor(x => x.Plan).Equal(PlanSuscripcion.Gratis).When(x => x.Estado == EstadoSuscripcion.Gratis)
            .WithMessage("En estado Gratis el plan es Gratis.");
        RuleFor(x => x.Nota).MaximumLength(500).WithMessage("La nota admite máximo 500 caracteres.");
        RuleFor(x => x.CodigoPromocional).MaximumLength(30).WithMessage("El código promocional no es válido.");
        RuleFor(x => x.Estado).Equal(EstadoSuscripcion.Activa).When(x => !string.IsNullOrWhiteSpace(x.CodigoPromocional))
            .WithMessage("El código promocional solo se aplica al activar un plan pagado.");
    }
}

/// <summary>
/// Plan de la empresa del token, sus límites y los pagos en línea con Wompi:
/// <list type="bullet">
/// <item>Sin fila (empresas anteriores a los planes) cuenta como prueba de Profesional desde hoy; se guarda al consultarla.</item>
/// <item>Prueba o periodo vencido → GRATIS (se guarda al leerla). Los datos se conservan; solo se limita crear o
/// activar vehículos y estudiantes por encima del plan.</item>
/// <item>El plan se activa únicamente con la confirmación de Wompi (evento firmado o consulta de la transacción),
/// verificando referencia, valor y moneda. Aplicar un pago es idempotente.</item>
/// <item>Pagar durante la prueba suma los días que quedaban; renovar el mismo plan suma al periodo vigente; cambiar
/// de plan empieza hoy.</item>
/// </list>
/// </summary>
public class ServicioSuscripcion(
    IBaseDatos db, IContextoUsuario contexto, IPasarelaPagos pasarela, IOptions<OpcionesWompi> wompi, TimeProvider reloj,
    ServicioCodigosPromocionales codigos, ILogger<ServicioSuscripcion> logger)
{
    private const string Moneda = "COP";
    private OpcionesWompi Wompi => wompi.Value;
    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    public static IReadOnlyList<PlanDto> Planes() =>
        CatalogoPlanes.Todos.Select(p => new PlanDto(
            p.Plan, p.Nombre, p.Descripcion, p.PrecioMensual, p.PrecioAnual, p.PrecioDesde, p.Destacado,
            new LimitesDto(p.Vehiculos, p.Estudiantes), p.Funciones, p.Incluye)).ToList();

    /// <summary>Prueba nueva para una empresa recién creada (en el mismo SaveChanges que la empresa).</summary>
    public static Suscripcion Prueba(Guid idEmpresa, DateTime ahora) => new()
    {
        IdEmpresa = idEmpresa,
        Plan = CatalogoPlanes.PlanPrueba,
        Estado = EstadoSuscripcion.Prueba,
        FechaInicio = ahora,
        FechaFin = ahora.AddMonths(CatalogoPlanes.MesesPrueba),
        PruebaUsada = true,
    };

    // --- Empresa -----------------------------------------------------------------------------------

    public async Task<SuscripcionDto> ObtenerAsync(CancellationToken ct = default)
    {
        var s = await PropiaAsync(ct);
        return await DtoAsync(s, ct);
    }

    public async Task<IReadOnlyList<PagoSuscripcionDto>> PagosAsync(CancellationToken ct = default) =>
        await db.PagosSuscripcion.AsNoTracking().OrderByDescending(p => p.FechaCreacion).Take(100)
            .Select(p => new PagoSuscripcionDto(p.Id, p.Referencia, p.Plan, p.Periodo, p.ValorCentavos / 100, p.Estado, p.MetodoPago ?? "",
                p.IdTransaccion ?? "", p.FechaCreacion, p.FechaAplicacion, p.CodigoPromocional ?? "", p.DescuentoCentavos / 100))
            .ToListAsync(ct);

    /// <summary>
    /// Valida un código promocional para el plan y periodo elegidos (422 con el motivo si no aplica) y devuelve el
    /// descuento, que es SOLO sobre el primer mes facturado.
    /// </summary>
    public async Task<ValidacionCodigoDto> ValidarCodigoAsync(SolicitudValidacionCodigo s, CancellationToken ct = default)
    {
        var idEmpresa = contexto.IdEmpresa ?? throw new ErrorNegocio("Inicia sesión de nuevo.", CodigoError.Prohibido);
        return (await codigos.EvaluarAsync(s.Codigo, idEmpresa, s.Plan!.Value, s.Periodo!.Value, ct)).Resultado;
    }

    /// <summary>
    /// Crea el intento de pago (PENDIENTE) y devuelve el enlace firmado al Web Checkout de Wompi. Con código
    /// promocional el valor ya trae el descuento del primer mes; si el total queda en $0 el plan se activa de una vez
    /// (sin Wompi) y <c>url</c> va vacía.
    /// </summary>
    public async Task<CheckoutDto> IniciarPagoAsync(SolicitudPagoSuscripcion s, CancellationToken ct = default)
    {
        var plan = CatalogoPlanes.De(s.Plan!.Value);
        var periodo = s.Periodo!.Value;
        var idEmpresa = contexto.IdEmpresa ?? throw new ErrorNegocio("Inicia sesión de nuevo.", CodigoError.Prohibido);
        var evaluacion = Texto.Opcional(s.CodigoPromocional) is { } texto ? await codigos.EvaluarAsync(texto, idEmpresa, plan.Plan, periodo, ct) : default;
        var total = evaluacion.Resultado?.Total ?? plan.Precio(periodo);
        if (total > 0 && !Wompi.Habilitada)
        {
            throw new ErrorNegocio("Los pagos en línea aún no están habilitados. Escríbenos desde Soporte y activamos tu plan.", CodigoError.Validacion);
        }
        var idUsuario = contexto.IdUsuario;
        var correo = idUsuario is { } id ? await db.Usuarios.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct) : null;
        var pago = new PagoSuscripcion
        {
            IdEmpresa = idEmpresa,
            Referencia = $"RR-{Guid.CreateVersion7():N}",
            Plan = plan.Plan,
            Periodo = periodo,
            ValorCentavos = total * 100,
            IdUsuario = idUsuario,
            CorreoPagador = Texto.Opcional(correo),
            Ambiente = Wompi.Habilitada ? Wompi.Ambiente : null,
            IdCodigoPromocional = evaluacion.Codigo?.Id,
            CodigoPromocional = evaluacion.Codigo?.Codigo,
            DescuentoCentavos = (evaluacion.Resultado?.Descuento ?? 0) * 100,
        };
        db.PagosSuscripcion.Add(pago);
        if (total == 0)
        {
            // El código cubre todo el primer pago: no hay nada que cobrar en Wompi.
            pago.Estado = EstadoPagoSuscripcion.Aprobado;
            pago.MetodoPago = "CODIGO_PROMOCIONAL";
            var suscripcion = await db.Suscripciones.FirstOrDefaultAsync(ct);
            if (suscripcion is null)
            {
                suscripcion = new Suscripcion { IdEmpresa = idEmpresa, PruebaUsada = true };
                db.Suscripciones.Add(suscripcion);
            }
            Activar(suscripcion, pago);
            await db.SaveChangesAsync(ct);
            return new CheckoutDto(pago.Referencia, "", 0, Moneda, "", "", "");
        }
        await db.SaveChangesAsync(ct);

        var firma = FirmasWompi.Integridad(pago.Referencia, pago.ValorCentavos, Moneda, Wompi.SecretoIntegridad);
        var retorno = $"{Wompi.UrlRetorno.TrimEnd('/')}/#/sistema/membership?pago={pago.Referencia}";
        var parametros = new List<(string, string)>
        {
            ("public-key", Wompi.LlavePublica), ("currency", Moneda), ("amount-in-cents", pago.ValorCentavos.ToString()),
            ("reference", pago.Referencia), ("signature:integrity", firma), ("redirect-url", retorno),
        };
        if (pago.CorreoPagador is { } c) parametros.Add(("customer-data:email", c));
        var url = "https://checkout.wompi.co/p/?" + string.Join('&', parametros.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
        return new CheckoutDto(pago.Referencia, url, pago.ValorCentavos, Moneda, Wompi.LlavePublica, firma, retorno);
    }

    /// <summary>
    /// Al volver de Wompi: consulta la transacción en Wompi (no confía en la redirección) y la aplica. Sin id de
    /// transacción solo devuelve el estado actual del pago (lo actualizará el evento).
    /// </summary>
    public async Task<PagoSuscripcionDto> VerificarPagoAsync(string referencia, SolicitudVerificacionPago s, CancellationToken ct = default)
    {
        var pago = await db.PagosSuscripcion.AsNoTracking().FirstOrDefaultAsync(p => p.Referencia == referencia, ct)
            ?? throw ErrorNegocio.NoEncontrado("El pago");
        if (Texto.Opcional(s.IdTransaccion) is { } idTransaccion && pago.Estado == EstadoPagoSuscripcion.Pendiente && Wompi.Habilitada)
        {
            TransaccionPasarela? transaccion = null;
            try
            {
                transaccion = await pasarela.ConsultarAsync(idTransaccion, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo consultar la transacción en Wompi");
            }
            if (transaccion is not null && transaccion.Referencia == referencia)
            {
                await AplicarTransaccionAsync(transaccion, ct);
            }
        }
        return await db.PagosSuscripcion.AsNoTracking().Where(p => p.Id == pago.Id)
            .Select(p => new PagoSuscripcionDto(p.Id, p.Referencia, p.Plan, p.Periodo, p.ValorCentavos / 100, p.Estado, p.MetodoPago ?? "",
                p.IdTransaccion ?? "", p.FechaCreacion, p.FechaAplicacion, p.CodigoPromocional ?? "", p.DescuentoCentavos / 100))
            .FirstAsync(ct);
    }

    // --- Evento de Wompi (anónimo, firmado) ------------------------------------------------------

    /// <summary>
    /// <c>transaction.updated</c>: verifica la firma con el secreto de eventos y aplica la transacción. false =
    /// firma inválida (401). Un evento de otro tipo o de una referencia desconocida se acepta y se ignora.
    /// </summary>
    public async Task<bool> ProcesarEventoAsync(JsonElement evento, CancellationToken ct = default)
    {
        if (!FirmasWompi.EventoValido(evento, Wompi.SecretoEventos))
        {
            logger.LogWarning("Evento de Wompi con firma inválida");
            return false;
        }
        if (FirmasWompi.Transaccion(evento) is { } transaccion)
        {
            await AplicarTransaccionAsync(transaccion, ct);
        }
        return true;
    }

    private async Task AplicarTransaccionAsync(TransaccionPasarela t, CancellationToken ct)
    {
        db.UsarModoSistema();
        var pago = await db.PagosSuscripcion.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Referencia == t.Referencia, ct);
        if (pago is null)
        {
            logger.LogWarning("Transacción de Wompi con una referencia desconocida");
            return;
        }
        if (t.ValorCentavos != pago.ValorCentavos || !t.Moneda.Equals(Moneda, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Transacción de Wompi {Referencia} con valor o moneda distintos al pago", pago.Referencia);
            return;
        }
        var estado = t.Estado.ToUpperInvariant() switch
        {
            "APPROVED" => EstadoPagoSuscripcion.Aprobado,
            "DECLINED" => EstadoPagoSuscripcion.Rechazado,
            "VOIDED" => EstadoPagoSuscripcion.Anulado,
            "ERROR" => EstadoPagoSuscripcion.Error,
            _ => EstadoPagoSuscripcion.Pendiente,
        };
        // Un pago aprobado y aplicado no retrocede (eventos repetidos o fuera de orden).
        if (pago.FechaAplicacion is not null)
        {
            return;
        }
        pago.Estado = estado;
        pago.IdTransaccion = t.Id;
        pago.MetodoPago = Texto.Opcional(t.MetodoPago);
        if (estado == EstadoPagoSuscripcion.Aprobado)
        {
            var s = await db.Suscripciones.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.IdEmpresa == pago.IdEmpresa, ct);
            if (s is null)
            {
                s = new Suscripcion { IdEmpresa = pago.IdEmpresa, PruebaUsada = true };
                db.Suscripciones.Add(s);
            }
            Activar(s, pago);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Aplica un pago aprobado al plan (y registra el uso de su código promocional). Pagar durante la prueba o
    /// renovar el mismo plan suma al periodo vigente; cambiar de plan empieza hoy.
    /// </summary>
    private void Activar(Suscripcion s, PagoSuscripcion pago)
    {
        var ahora = Ahora;
        var vigente = s.FechaFin is { } fin && fin > ahora && s.Estado != EstadoSuscripcion.Gratis;
        var desde = vigente && (s.Estado == EstadoSuscripcion.Prueba || s.Plan == pago.Plan) ? s.FechaFin!.Value : ahora;
        s.Plan = pago.Plan;
        s.Estado = EstadoSuscripcion.Activa;
        s.Periodo = pago.Periodo;
        s.FechaInicio = ahora;
        s.FechaFin = pago.Periodo == PeriodoPago.Anual ? desde.AddYears(1) : desde.AddMonths(1);
        s.PruebaUsada = true;
        pago.FechaAplicacion = ahora;
        if (pago.IdCodigoPromocional is { } idCodigo)
        {
            db.UsosCodigoPromocional.Add(new UsoCodigoPromocional
            {
                IdEmpresa = pago.IdEmpresa, IdCodigoPromocional = idCodigo, IdPagoSuscripcion = pago.Id, Plan = pago.Plan, Periodo = pago.Periodo,
                DescuentoCentavos = pago.DescuentoCentavos,
            });
        }
    }

    // --- Límites (los usan otros módulos) ----------------------------------------------------------

    /// <summary>Plan vigente de la empresa del contexto (sin guardar nada).</summary>
    public async Task<DefinicionPlan> PlanVigenteAsync(CancellationToken ct = default)
    {
        var s = await db.Suscripciones.AsNoTracking().FirstOrDefaultAsync(ct);
        return CatalogoPlanes.De(s is null ? CatalogoPlanes.PlanPrueba : Vencida(s) ? PlanSuscripcion.Gratis : s.Plan);
    }

    public async Task<bool> TieneFuncionAsync(string funcion, CancellationToken ct = default) =>
        (await PlanVigenteAsync(ct)).Tiene(funcion);

    /// <summary>422 si activar uno más supera el límite de vehículos del plan.</summary>
    public async Task ValidarVehiculoAsync(Guid? idActual, CancellationToken ct = default)
    {
        var plan = await PlanVigenteAsync(ct);
        if (plan.Vehiculos is { } limite && await db.Vehiculos.CountAsync(v => v.Activo && v.Id != idActual, ct) >= limite)
        {
            throw new ErrorNegocio(
                $"Tu plan {plan.Nombre} permite {limite} vehículo(s) activo(s). Inactiva uno o mejora tu plan en Sistema › Planes y membresías.",
                CodigoError.Validacion);
        }
    }

    /// <summary>422 si activar uno más supera el límite de estudiantes del plan.</summary>
    public async Task ValidarEstudianteAsync(Guid? idActual, CancellationToken ct = default)
    {
        var plan = await PlanVigenteAsync(ct);
        if (plan.Estudiantes is { } limite && await db.Estudiantes.CountAsync(e => e.Activo && e.Id != idActual, ct) >= limite)
        {
            throw new ErrorNegocio(
                $"Tu plan {plan.Nombre} permite {limite} estudiantes activos. Inactiva alguno o mejora tu plan en Sistema › Planes y membresías.",
                CodigoError.Validacion);
        }
    }

    // --- Plataforma --------------------------------------------------------------------------------

    public async Task<SuscripcionDto> DeEmpresaAsync(Guid idEmpresa, CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var s = await DeEmpresaInternaAsync(idEmpresa, ct);
        return await DtoAsync(s, ct, idEmpresa);
    }

    public async Task<IReadOnlyList<PagoSuscripcionDto>> PagosDeEmpresaAsync(Guid idEmpresa, CancellationToken ct = default) =>
        await db.PagosSuscripcion.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking().Where(p => p.IdEmpresa == idEmpresa)
            .OrderByDescending(p => p.FechaCreacion).Take(100)
            .Select(p => new PagoSuscripcionDto(p.Id, p.Referencia, p.Plan, p.Periodo, p.ValorCentavos / 100, p.Estado, p.MetodoPago ?? "",
                p.IdTransaccion ?? "", p.FechaCreacion, p.FechaAplicacion, p.CodigoPromocional ?? "", p.DescuentoCentavos / 100))
            .ToListAsync(ct);

    public async Task<SuscripcionDto> AjustarAsync(Guid idEmpresa, SolicitudAjusteSuscripcion a, CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var s = await DeEmpresaInternaAsync(idEmpresa, ct);
        s.Plan = a.Plan!.Value;
        s.Estado = a.Estado!.Value;
        s.Periodo = a.Estado == EstadoSuscripcion.Activa ? a.Periodo ?? PeriodoPago.Mensual : null;
        s.FechaFin = a.Estado == EstadoSuscripcion.Gratis ? null : DateTime.SpecifyKind(a.FechaFin!.Value.ToUniversalTime(), DateTimeKind.Utc);
        if (s.FechaFin is { } fin && fin <= Ahora)
        {
            throw new ErrorNegocio("La fecha de vencimiento debe ser futura.", CodigoError.Validacion);
        }
        s.FechaInicio = Ahora;
        s.Nota = Texto.Opcional(a.Nota);
        if (Texto.Opcional(a.CodigoPromocional) is { } texto)
        {
            // Plan activado a mano con un código (p. ej. pago por transferencia): cuenta como uso y queda anotado.
            var (codigo, r) = await codigos.EvaluarAsync(texto, idEmpresa, s.Plan, s.Periodo!.Value, ct);
            db.UsosCodigoPromocional.Add(new UsoCodigoPromocional
            {
                IdEmpresa = idEmpresa, IdCodigoPromocional = codigo.Id, Plan = s.Plan, Periodo = s.Periodo.Value, DescuentoCentavos = r.Descuento * 100,
            });
            var anotacion = $"Código {codigo.Codigo}: descuento de {Pesos(r.Descuento)} en el primer mes (total {Pesos(r.Total)}).";
            var nota = s.Nota is null ? anotacion : $"{s.Nota} · {anotacion}";
            s.Nota = nota.Length > 500 ? nota[..500] : nota;
        }
        await db.SaveChangesAsync(ct);
        return await DtoAsync(s, ct, idEmpresa);
    }

    // --- Apoyo -------------------------------------------------------------------------------------

    private static string Pesos(long valor) => "$" + valor.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"));

    private bool Vencida(Suscripcion s) => s.Estado != EstadoSuscripcion.Gratis && (s.FechaFin is null || s.FechaFin <= Ahora);

    /// <summary>La suscripción de la empresa del token: la crea (prueba) si no existe y la baja a Gratis si venció.</summary>
    private async Task<Suscripcion> PropiaAsync(CancellationToken ct)
    {
        var idEmpresa = contexto.IdEmpresa ?? throw new ErrorNegocio("Inicia sesión de nuevo.", CodigoError.Prohibido);
        var s = await db.Suscripciones.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = Prueba(idEmpresa, Ahora);
            db.Suscripciones.Add(s);
            await db.SaveChangesAsync(ct);
        }
        await BajarSiVencioAsync(s, ct);
        return s;
    }

    private async Task<Suscripcion> DeEmpresaInternaAsync(Guid idEmpresa, CancellationToken ct)
    {
        if (!await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(e => e.Id == idEmpresa, ct))
        {
            throw ErrorNegocio.NoEncontrado("La empresa");
        }
        var s = await db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]).FirstOrDefaultAsync(x => x.IdEmpresa == idEmpresa, ct);
        if (s is null)
        {
            s = Prueba(idEmpresa, Ahora);
            db.Suscripciones.Add(s);
            await db.SaveChangesAsync(ct);
        }
        await BajarSiVencioAsync(s, ct);
        return s;
    }

    private async Task BajarSiVencioAsync(Suscripcion s, CancellationToken ct)
    {
        if (!Vencida(s)) return;
        s.Plan = PlanSuscripcion.Gratis;
        s.Estado = EstadoSuscripcion.Gratis;
        s.Periodo = null;
        s.FechaInicio = Ahora;
        s.FechaFin = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task<SuscripcionDto> DtoAsync(Suscripcion s, CancellationToken ct, Guid? idEmpresa = null)
    {
        var plan = CatalogoPlanes.De(s.Plan);
        IQueryable<Domain.Operacion.Vehiculo> vehiculos = db.Vehiculos;
        IQueryable<Domain.Personas.Estudiante> estudiantes = db.Estudiantes;
        if (idEmpresa is { } id)
        {
            vehiculos = db.Vehiculos.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(v => v.IdEmpresa == id);
            estudiantes = db.Estudiantes.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(e => e.IdEmpresa == id);
        }
        var uso = new UsoDto(await vehiculos.CountAsync(v => v.Activo, ct), await estudiantes.CountAsync(e => e.Activo, ct));
        int? dias = s.FechaFin is { } fin ? Math.Max(0, (int)Math.Ceiling((fin - Ahora).TotalDays)) : null;
        return new SuscripcionDto(
            s.Plan, plan.Nombre, s.Estado, s.Periodo is { } p ? TextoEnum.Escribir(p) : "", s.FechaInicio, s.FechaFin, dias, s.PruebaUsada,
            new LimitesDto(plan.Vehiculos, plan.Estudiantes), uso, plan.Funciones, Wompi.Habilitada, s.Nota ?? "");
    }
}
