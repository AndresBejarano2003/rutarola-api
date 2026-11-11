using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Suscripciones;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Suscripciones;

/// <summary>Códigos promocionales: la plataforma los crea; la empresa los usa al pagar y solo descuentan el primer mes.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class CodigosPromocionalesTests(BaseDatosPrueba bd)
{
    private const string Url = "/api/v1/plataforma/codigos-promocionales";
    private static string Hoy(int dias = 0) => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)).AddDays(dias).ToString("yyyy-MM-dd");

    private static Dictionary<string, object?> Codigo(string codigo, string tipo = "PORCENTAJE", decimal valor = 20, string alcance = "TODAS",
        string[]? planes = null, string? periodo = null, int? usosMaximos = null, int usosPorEmpresa = 1, string? inicio = null, string? fin = null,
        Guid[]? empresas = null, string estado = "ACTIVO") => new()
    {
        ["codigo"] = codigo, ["descripcion"] = "Promoción de prueba", ["tipoDescuento"] = tipo, ["valor"] = valor,
        ["fechaInicio"] = inicio ?? Hoy(-1), ["fechaFin"] = fin ?? Hoy(30), ["alcance"] = alcance, ["planes"] = planes ?? [],
        ["periodo"] = periodo, ["usosMaximos"] = usosMaximos, ["usosPorEmpresa"] = usosPorEmpresa, ["estado"] = estado, ["empresaIds"] = empresas ?? [],
    };

    private static string Unico(string prefijo) => $"{prefijo}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static Task<HttpResponseMessage> ValidarAsync(HttpClient c, string codigo, string plan = "PROFESIONAL", string periodo = "MENSUAL") =>
        c.PostAsJsonAsync("/api/v1/suscripcion/codigo", new { codigo, plan, periodo }, ClienteApi.Json);

    private async Task<CodigoPromocionalDto> CrearAsync(HttpClient super, Dictionary<string, object?> solicitud) =>
        await (await super.PostAsJsonAsync(Url, solicitud, ClienteApi.Json)).LeerAsync<CodigoPromocionalDto>(HttpStatusCode.Created);

    [Fact]
    public async Task El_descuento_es_solo_del_primer_mes_y_cada_empresa_lo_usa_una_vez()
    {
        var super = await bd.SuperAdminAsync();
        var texto = Unico("pro20");
        var codigo = await CrearAsync(super, Codigo(texto.ToLowerInvariant(), planes: ["PROFESIONAL"]));
        Assert.Equal((texto, "VIGENTE", 0), (codigo.Codigo, codigo.Vigencia, codigo.Usos));

        var admin = await bd.AdminNuevoAsync();
        // Una empresa no administra códigos.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Cliente.GetAsync(Url)).StatusCode);
        Assert.Contains("no aplica para el plan Esencial",
            await (await ValidarAsync(admin.Cliente, texto, "ESENCIAL")).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Contains("no existe", await (await ValidarAsync(admin.Cliente, "NO-EXISTE")).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        // Anual: el 20 % se calcula sobre UNA mensualidad (189.900), no sobre el año.
        var v = await (await ValidarAsync(admin.Cliente, texto.ToLowerInvariant(), periodo: "ANUAL")).LeerAsync<ValidacionCodigoDto>();
        Assert.Equal((1_899_000L, 37_980L, 1_861_020L), (v.Precio, v.Descuento, v.Total));

        var checkout = await (await admin.Cliente.PostAsJsonAsync("/api/v1/suscripcion/pagos",
            new { plan = "PROFESIONAL", periodo = "ANUAL", codigoPromocional = texto }, ClienteApi.Json)).LeerAsync<CheckoutDto>(HttpStatusCode.Created);
        Assert.Equal(1_861_020L * 100, checkout.ValorCentavos);
        // Mientras el pago no se aprueba no cuenta como uso.
        Assert.Equal(HttpStatusCode.OK, (await ValidarAsync(admin.Cliente, texto)).StatusCode);

        await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/pagos/wompi/eventos", SuscripcionTests.Evento(checkout.Referencia, checkout.ValorCentavos), ClienteApi.Json);
        var pago = Assert.Single(await (await admin.Cliente.GetAsync("/api/v1/suscripcion/pagos")).LeerAsync<List<PagoSuscripcionDto>>());
        Assert.Equal((EstadoPagoSuscripcion.Aprobado, texto, 37_980L, 1_861_020L), (pago.Estado, pago.CodigoPromocional, pago.Descuento, pago.Valor));
        Assert.Contains("ya usó", await (await ValidarAsync(admin.Cliente, texto)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        var uso = Assert.Single(await (await super.GetAsync($"{Url}/{codigo.Id}/usos")).LeerAsync<List<UsoCodigoDto>>());
        Assert.Equal((admin.Empresa.Id, checkout.Referencia, 37_980L), (uso.EmpresaId, uso.Referencia, uso.Descuento));
        Assert.Equal(1, (await (await super.GetAsync($"{Url}/{codigo.Id}")).LeerAsync<CodigoPromocionalDto>()).Usos);
        // Usado: no se elimina, se inactiva.
        Assert.NotNull((await (await super.GetAsync($"{Url}/{codigo.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>()).Motivo);
        Assert.Equal(HttpStatusCode.Conflict, (await super.DeleteAsync($"{Url}/{codigo.Id}")).StatusCode);
        var inactivo = Codigo(texto, planes: ["PROFESIONAL"], estado: "INACTIVO");
        await (await super.PutAsJsonAsync($"{Url}/{codigo.Id}", inactivo, ClienteApi.Json)).LeerAsync<CodigoPromocionalDto>();
        var otra = await bd.AdminNuevoAsync();
        Assert.Contains("no está activo", await (await ValidarAsync(otra.Cliente, texto)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Alcance_vigencia_limite_de_usos_y_un_codigo_del_cien_por_ciento_activa_sin_Wompi()
    {
        var super = await bd.SuperAdminAsync();
        var a = await bd.AdminNuevoAsync();
        var b = await bd.AdminNuevoAsync();

        var soloA = Unico("soloa");
        await CrearAsync(super, Codigo(soloA, empresas: [a.Empresa.Id], alcance: "EMPRESAS"));
        Assert.Equal(HttpStatusCode.OK, (await ValidarAsync(a.Cliente, soloA)).StatusCode);
        Assert.Contains("no aplica para tu empresa", await (await ValidarAsync(b.Cliente, soloA)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        var futuro = Unico("futuro");
        var programado = await CrearAsync(super, Codigo(futuro, inicio: Hoy(5), fin: Hoy(10)));
        Assert.Equal("PROGRAMADO", programado.Vigencia);
        Assert.Contains("es válido desde", await (await ValidarAsync(a.Cliente, futuro)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var vencido = Unico("vencido");
        await CrearAsync(super, Codigo(vencido, inicio: Hoy(-10), fin: Hoy(-1)));
        Assert.Contains("venció", await (await ValidarAsync(a.Cliente, vencido)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var soloAnual = Unico("anual");
        await CrearAsync(super, Codigo(soloAnual, periodo: "ANUAL"));
        Assert.Contains("solo aplica al pago anual", await (await ValidarAsync(a.Cliente, soloAnual)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var filtrados = await (await super.GetAsync($"{Url}?vigencia=VENCIDO&texto={vencido}")).LeerAsync<ListaPaginada<CodigoPromocionalDto>>();
        Assert.Equal(vencido, Assert.Single(filtrados.Items).Codigo);

        // Valor fijo mayor que un mes: se descuenta el mes completo y no hay nada que cobrar → el plan se activa ya.
        var gratis = Unico("mesgratis");
        await CrearAsync(super, Codigo(gratis, tipo: "VALOR_FIJO", valor: 500_000, usosMaximos: 1));
        var v = await (await ValidarAsync(a.Cliente, gratis, "ESENCIAL")).LeerAsync<ValidacionCodigoDto>();
        Assert.Equal((89_900L, 89_900L, 0L), (v.Precio, v.Descuento, v.Total));
        var checkout = await (await a.Cliente.PostAsJsonAsync("/api/v1/suscripcion/pagos",
            new { plan = "ESENCIAL", periodo = "MENSUAL", codigoPromocional = gratis }, ClienteApi.Json)).LeerAsync<CheckoutDto>(HttpStatusCode.Created);
        Assert.Equal((0L, ""), (checkout.ValorCentavos, checkout.Url));
        var s = await (await a.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Esencial, EstadoSuscripcion.Activa), (s.Plan, s.Estado));
        // Usos máximos (1) agotados para cualquier empresa.
        Assert.Contains("límite de usos", await (await ValidarAsync(b.Cliente, gratis, "ESENCIAL")).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task La_plataforma_aplica_el_codigo_al_activar_a_mano_y_primera_compra_solo_una_vez()
    {
        var super = await bd.SuperAdminAsync();
        var admin = await bd.AdminNuevoAsync();
        var texto = Unico("nuevo");
        var codigo = await CrearAsync(super, Codigo(texto, alcance: "PRIMERA_COMPRA", valor: 50, usosPorEmpresa: 3));
        Assert.Equal(HttpStatusCode.OK, (await ValidarAsync(admin.Cliente, texto)).StatusCode);

        var url = $"/api/v1/empresas/{admin.Empresa.Id}/suscripcion";
        var ajustada = await (await super.PutAsJsonAsync(url, new
        {
            plan = "PROFESIONAL", estado = "ACTIVA", periodo = "MENSUAL", fechaFin = DateTime.UtcNow.AddDays(30), nota = "Transferencia", codigoPromocional = texto,
        }, ClienteApi.Json)).LeerAsync<SuscripcionDto>();
        Assert.StartsWith("Transferencia · Código " + texto, ajustada.Nota);
        Assert.Contains("94.950", ajustada.Nota);
        Assert.Equal(1, (await (await super.GetAsync($"{Url}/{codigo.Id}")).LeerAsync<CodigoPromocionalDto>()).Usos);
        // Ya compró: aunque admite 3 usos por empresa, es solo para la primera compra.
        Assert.Contains("primera compra", await (await ValidarAsync(admin.Cliente, texto)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PutAsJsonAsync(url, new
        {
            plan = "GRATIS", estado = "GRATIS", codigoPromocional = texto,
        }, ClienteApi.Json)).StatusCode);
    }

    [Fact]
    public async Task Sin_codigo_el_sistema_lo_genera_con_el_prefijo_y_al_editar_se_conserva()
    {
        var super = await bd.SuperAdminAsync();
        var conPrefijo = Codigo("");
        conPrefijo["prefijo"] = "regreso";
        var a = await CrearAsync(super, conPrefijo);
        Assert.Matches("^REGRESO-[A-HJ-NP-Z2-9]{8}$", a.Codigo);
        var b = await CrearAsync(super, Codigo(""));
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", b.Codigo);
        var editado = await (await super.PutAsJsonAsync($"{Url}/{b.Id}", Codigo("", valor: 30), ClienteApi.Json)).LeerAsync<CodigoPromocionalDto>();
        Assert.Equal((b.Codigo, 30m), (editado.Codigo, editado.Valor));
        conPrefijo["prefijo"] = "con-guion";
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsJsonAsync(Url, conPrefijo, ClienteApi.Json)).StatusCode);
    }

    [Fact]
    public async Task Una_sesion_abierta_antes_de_un_permiso_nuevo_recibe_401_para_renovarse()
    {
        var super = await bd.SuperAdminAsync();
        var token = super.DefaultRequestHeaders.Authorization!.Parameter!;
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
        string Valor(string tipo) => claims.First(c => c.Type == tipo).Value;
        var permisos = claims.Where(c => c.Type == Reclamos.Permiso).Select(c => c.Value).Where(p => p != RutaRola.Domain.Seguridad.Permisos.PromocionesGestionar).ToList();
        var generador = (RutaRola.Application.Autenticacion.IGeneradorTokens)bd.Api.Services.GetService(typeof(RutaRola.Application.Autenticacion.IGeneradorTokens))!;
        var viejo = generador.CrearAcceso(new RutaRola.Application.Autenticacion.DatosToken(
            Guid.Parse(Valor(Reclamos.IdUsuario)), "Super", null, Valor(Reclamos.Rol), null, null, Guid.Parse(Valor(Reclamos.IdSesion)), permisos));
        var cliente = bd.Cliente(viejo.Token);
        var respuesta = await cliente.GetAsync(Url);
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains("invalid_token", respuesta.Headers.WwwAuthenticate.ToString());
        // Un permiso que el rol NO tiene sigue siendo 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await super.GetAsync("/api/v1/suscripcion")).StatusCode);
    }

    [Fact]
    public async Task Valida_los_datos_del_codigo_y_no_repite_el_texto()
    {
        var super = await bd.SuperAdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsJsonAsync(Url, Codigo("con espacio"), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsJsonAsync(Url, Codigo(Unico("x"), valor: 150), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsJsonAsync(Url, Codigo(Unico("x"), alcance: "EMPRESAS"), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsJsonAsync(Url, Codigo(Unico("x"), inicio: Hoy(5), fin: Hoy(1)), ClienteApi.Json)).StatusCode);
        var texto = Unico("dup");
        var creado = await CrearAsync(super, Codigo(texto));
        Assert.Equal(HttpStatusCode.Conflict, (await super.PostAsJsonAsync(Url, Codigo(texto.ToLowerInvariant()), ClienteApi.Json)).StatusCode);
        // Sin usos se puede eliminar.
        Assert.Equal(HttpStatusCode.NoContent, (await super.DeleteAsync($"{Url}/{creado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.GetAsync($"{Url}/{creado.Id}")).StatusCode);
    }
}
