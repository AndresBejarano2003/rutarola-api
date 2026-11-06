using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Seguridad;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Base;

/// <summary>Aislamiento por empresa a nivel de DbContext (las pruebas por HTTP llegan en la fase 2).</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class MultiempresaDbContextTests(BaseDatosPrueba bd)
{
    private async Task<(Guid A, Guid B, Guid ColegioA, Guid ColegioB)> CrearDosEmpresasAsync()
    {
        await using var db = bd.Contexto(null, modoSistema: true);
        var a = new Empresa { Nombre = "Empresa A", Nit = $"A-{Guid.NewGuid():N}"[..20] };
        var b = new Empresa { Nombre = "Empresa B", Nit = $"B-{Guid.NewGuid():N}"[..20] };
        var colegioA = new Colegio { IdEmpresa = a.Id, Nit = "800-A", RazonSocial = "Colegio A", Direccion = "Calle 1" };
        var colegioB = new Colegio { IdEmpresa = b.Id, Nit = "800-B", RazonSocial = "Colegio B", Direccion = "Calle 2" };
        colegioB.Cursos.Add(new ColegioCurso { Grado = "1°", HoraEntrada = new(7, 0), HoraSalida = new(13, 0) });
        db.AddRange(a, b, colegioA, colegioB);
        await db.SaveChangesAsync();
        return (a.Id, b.Id, colegioA.Id, colegioB.Id);
    }

    [Fact]
    public async Task Un_usuario_de_A_solo_ve_los_registros_de_A_y_sus_hijos()
    {
        var (a, _, colegioA, colegioB) = await CrearDosEmpresasAsync();
        await using var db = bd.Contexto(a);

        var colegios = await db.Colegios.Select(c => c.Id).ToListAsync();
        Assert.Contains(colegioA, colegios);
        Assert.DoesNotContain(colegioB, colegios);
        Assert.Null(await db.Colegios.FindAsync(colegioB));
        Assert.Null(await db.Colegios.FirstOrDefaultAsync(x => x.Id == colegioB));
        Assert.False(await db.ColegiosCursos.AnyAsync(x => x.IdColegio == colegioB));
        Assert.Equal([a], await db.Empresas.Select(e => e.Id).ToListAsync());
    }

    [Fact]
    public async Task Sin_empresa_en_el_token_no_se_ve_nada()
    {
        await CrearDosEmpresasAsync();
        await using var db = bd.Contexto(null);
        Assert.False(await db.Colegios.AnyAsync());
        Assert.False(await db.Usuarios.AnyAsync());
    }

    [Fact]
    public async Task Al_crear_la_empresa_sale_del_token_aunque_el_registro_traiga_otra()
    {
        var (a, b, _, _) = await CrearDosEmpresasAsync();
        await using (var db = bd.Contexto(a))
        {
            db.Colegios.Add(new Colegio { IdEmpresa = b, Nit = "800-X", RazonSocial = "Intruso", Direccion = "Calle 3" });
            await db.SaveChangesAsync();
        }

        await using var sistema = bd.Contexto(null, modoSistema: true);
        var creado = await sistema.Colegios.SingleAsync(c => c.RazonSocial == "Intruso" && (c.IdEmpresa == a || c.IdEmpresa == b));
        Assert.Equal(a, creado.IdEmpresa);
    }

    [Fact]
    public async Task Un_usuario_de_A_no_puede_modificar_ni_eliminar_registros_de_B()
    {
        var (a, b, _, colegioB) = await CrearDosEmpresasAsync();
        Colegio ajeno;
        await using (var sistema = bd.Contexto(null, modoSistema: true))
        {
            ajeno = await sistema.Colegios.AsNoTracking().SingleAsync(c => c.Id == colegioB);
        }

        await using (var db = bd.Contexto(a))
        {
            ajeno.RazonSocial = "Modificado por A";
            db.Colegios.Update(ajeno);
            var error = await Assert.ThrowsAsync<ErrorNegocio>(() => db.SaveChangesAsync());
            Assert.Equal(CodigoError.Prohibido, error.Codigo);
        }

        await using (var db = bd.Contexto(a))
        {
            db.Colegios.Remove(ajeno);
            await Assert.ThrowsAsync<ErrorNegocio>(() => db.SaveChangesAsync());
        }

        await using var verificacion = bd.Contexto(b);
        Assert.Equal("Colegio B", (await verificacion.Colegios.SingleAsync(c => c.Id == colegioB)).RazonSocial);
    }

    [Fact]
    public async Task No_se_puede_mover_un_registro_propio_a_otra_empresa()
    {
        var (a, b, colegioA, _) = await CrearDosEmpresasAsync();
        await using var db = bd.Contexto(a);
        var propio = await db.Colegios.SingleAsync(c => c.Id == colegioA);
        propio.IdEmpresa = b;
        await Assert.ThrowsAsync<ErrorNegocio>(() => db.SaveChangesAsync());
    }
}
