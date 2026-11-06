using Npgsql;
using RutaRola.Infrastructure.Persistencia;

namespace RutaRola.Tests.Base;

public class CadenaConexionTests
{
    [Fact]
    public void Convierte_la_url_del_proveedor_al_formato_de_Npgsql()
    {
        var b = new NpgsqlConnectionStringBuilder(CadenaConexion.Normalizar("postgresql://ruta_user:cl%40ve@dpg-abc.oregon-postgres.render.com/rutarola_db?sslmode=require"));
        Assert.Equal("dpg-abc.oregon-postgres.render.com", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("rutarola_db", b.Database);
        Assert.Equal("ruta_user", b.Username);
        Assert.Equal("cl@ve", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Fact]
    public void Deja_igual_el_formato_de_Npgsql()
    {
        const string cadena = "Host=db;Port=5432;Database=rutarola;Username=rutarola;Password=x";
        Assert.Equal(cadena, CadenaConexion.Normalizar(cadena));
    }
}
