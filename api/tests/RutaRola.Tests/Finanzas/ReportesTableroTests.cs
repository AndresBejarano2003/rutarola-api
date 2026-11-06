using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Finanzas;

/// <summary>Reportes y tablero con fechas relativas a hoy: factura de -3 a +3 meses con día límite 31.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class ReportesTableroTests(BaseDatosPrueba bd)
{
    private async Task<(DatosCatalogos.Admin Admin, FacturaDto Factura)> EscenarioAsync()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id));
        // Día 31: la cuota del mes en curso siempre está dentro del plazo; las anteriores ya vencieron.
        await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(factura.Id, dia: 31));
        await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, DatosFinanzas.Periodo(-1), medio: "NEQUI"));
        return (admin, factura);
    }

    [Fact]
    public async Task Calcula_pendientes_morosos_e_ingresos()
    {
        var (admin, factura) = await EscenarioAsync();
        var c = admin.Cliente;
        await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, DatosFinanzas.Periodo(-3), 400000, "EFECTIVO"));

        var pendientes = await (await c.GetAsync("/api/v1/reportes/facturas-pendientes")).LeerAsync<List<CuotaPendienteDto>>();
        Assert.Equal([DatosFinanzas.Periodo(-2), DatosFinanzas.Periodo()], pendientes.Select(p => p.Periodo));
        Assert.True(pendientes[0].DiasMora > 0);
        Assert.True(pendientes[1].DiasMora <= 0);
        var limite = DatosOperacion.Hoy.AddMonths(-2);
        Assert.Equal(new DateOnly(limite.Year, limite.Month, DateTime.DaysInMonth(limite.Year, limite.Month)), pendientes[0].FechaLimite);
        Assert.Single(await (await c.GetAsync("/api/v1/reportes/facturas-pendientes?situacion=VENCIDAS&texto=juan")).LeerAsync<List<CuotaPendienteDto>>());
        Assert.Single(await (await c.GetAsync("/api/v1/reportes/facturas-pendientes?situacion=POR_VENCER")).LeerAsync<List<CuotaPendienteDto>>());

        var moroso = Assert.Single(await (await c.GetAsync("/api/v1/reportes/morosos")).LeerAsync<List<MorosoDto>>());
        Assert.Equal(("María Pérez López", "3001234567", 1, 420000m), (moroso.NombreAcudiente, moroso.Telefono, moroso.Cuotas, moroso.ValorAdeudado));
        Assert.Equal([factura.Numero], moroso.Facturas);
        Assert.Empty(await (await c.GetAsync("/api/v1/reportes/morosos?diasMinimos=90")).LeerAsync<List<MorosoDto>>());

        var mes = DatosFinanzas.Periodo();
        var ingresos = await (await c.GetAsync($"/api/v1/reportes/ingresos?desde={mes}&hasta={mes}")).LeerAsync<ReporteIngresosDto>();
        Assert.Equal((820000m, 2, 410000m), (ingresos.Total, ingresos.Cantidad, ingresos.Promedio));
        Assert.Equal(mes, Assert.Single(ingresos.PorMes).Periodo);
        Assert.Equal([MetodoPago.Efectivo, MetodoPago.Nequi], ingresos.PorMedio.Select(m => m.MedioPago));
        var soloNequi = await (await c.GetAsync("/api/v1/reportes/ingresos?medioPago=NEQUI")).LeerAsync<ReporteIngresosDto>();
        Assert.Equal(420000m, Assert.Single(soloNequi.Recibos).Valor);
    }

    [Fact]
    public async Task El_tablero_resume_recaudo_cartera_y_pendientes()
    {
        var (admin, factura) = await EscenarioAsync();
        var c = admin.Cliente;
        await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, DatosFinanzas.Periodo(), 410000));

        var t = await (await c.GetAsync("/api/v1/tablero")).LeerAsync<TableroDto>();
        Assert.Equal(DatosFinanzas.Periodo(), t.Periodo);
        Assert.Equal(new RecaudoDto(410000, 420000, 1, 420000), t.Recaudo);
        Assert.Equal(new CarteraDto(840000, 2, 1, 0), t.Cartera);
        Assert.Equal(new EstudiantesTableroDto(1, 1, 1, 1), t.Estudiantes);
        Assert.Equal(new OperacionTableroDto(0, 0, 0, 0, null), t.Operacion);
        Assert.Equal(["mora", "sin-ruta"], t.Pendientes.Select(p => p.Id));
        Assert.Equal("$ 840.000 en 2 cuotas vencidas", t.Pendientes[0].Detalle);
        Assert.Equal(NivelNotificacion.Critica, t.Pendientes[0].Nivel);
        Assert.Equal(2, t.UltimosPagos.Count);
        Assert.Equal(12, t.Historico.Count);
        Assert.Equal(new HistoricoDto(DatosFinanzas.Periodo(), 420000, 410000), t.Historico[^1]);
        Assert.Equal("Colegio San Cayetano", Assert.Single(t.EstudiantesPorColegio).Colegio);

        // Otra empresa ve su tablero vacío.
        var otra = await bd.AdminNuevoAsync();
        var vacio = await (await otra.Cliente.GetAsync("/api/v1/tablero")).LeerAsync<TableroDto>();
        Assert.Equal(new RecaudoDto(0, 0, 0, 0), vacio.Recaudo);
        Assert.Empty(vacio.Pendientes);
    }

    [Fact]
    public async Task El_tablero_incluye_operacion_y_agenda()
    {
        var e = await bd.EscenarioAsync(estudiantes: 2);
        var c = e.Admin.Cliente;
        await c.CrearAsync<Application.Operacion.VehiculoDto>("vehiculos",
            DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "9", DatosOperacion.Hoy.AddDays(10).ToString("yyyy-MM-dd")));

        var t = await (await c.GetAsync("/api/v1/tablero")).LeerAsync<TableroDto>();
        Assert.Equal(new OperacionTableroDto(1, 0, 2, 1, 11), t.Operacion); // 2 de 19 puestos.
        Assert.Equal(new OcupacionRutaDto($"{e.Ruta.Codigo} · Norte", 2, 19), Assert.Single(t.OcupacionRutas));
        Assert.Contains(t.Pendientes, p => p.Id == "vehiculos-por-vencer" && p.Cantidad == 1);
        var soat = Assert.Single(t.Agenda);
        Assert.Equal(("DOCUMENTO", "Vence SOAT", DatosOperacion.Hoy.AddDays(10)), (soat.Tipo, soat.Titulo, soat.Fecha));
    }
}
