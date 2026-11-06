using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comun;

namespace RutaRola.Infrastructure.Seguridad;

/// <summary>JWT HS256 con los claims de <see cref="Reclamos"/>; refresh tokens aleatorios guardados como SHA-256.</summary>
public class GeneradorTokens(IOptions<OpcionesJwt> opciones, TimeProvider reloj) : IGeneradorTokens
{
    private readonly OpcionesJwt jwt = opciones.Value;
    private readonly JsonWebTokenHandler manejador = new();

    public static SymmetricSecurityKey Llave(string clave) => new(Encoding.UTF8.GetBytes(clave));

    public TokenAcceso CrearAcceso(DatosToken datos)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var expira = ahora.AddMinutes(jwt.MinutosAcceso);

        var claims = new List<Claim>
        {
            new(Reclamos.IdUsuario, datos.IdUsuario.ToString()),
            new(Reclamos.IdSesion, datos.IdSesion.ToString()),
            new(Reclamos.Nombre, datos.Nombre),
            new(Reclamos.Rol, datos.Rol),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (datos.IdEmpresa is { } empresa) claims.Add(new(Reclamos.IdEmpresa, empresa.ToString()));
        if (datos.IdEmpleado is { } empleado) claims.Add(new(Reclamos.IdEmpleado, empleado.ToString()));
        if (datos.IdAcudiente is { } acudiente) claims.Add(new(Reclamos.IdAcudiente, acudiente.ToString()));
        claims.AddRange(datos.Permisos.Select(p => new Claim(Reclamos.Permiso, p)));

        var token = manejador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Emisor,
            Audience = jwt.Audiencia,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(Llave(jwt.Clave), SecurityAlgorithms.HmacSha256),
        });
        return new TokenAcceso(token, expira);
    }

    public string CrearRefresh() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public string Hash(string refreshToken) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
