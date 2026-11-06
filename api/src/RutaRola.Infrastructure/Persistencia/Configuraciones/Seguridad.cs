using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class EmpresaConfig : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> b)
    {
        b.Tabla("empresas", "id_empresa");
        b.Property(x => x.Nombre).HasMaxLength(150);
        b.Property(x => x.Nit).HasMaxLength(20);
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.Ciudad).HasMaxLength(100);
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.HasIndex(x => x.Nit).IsUnique();
    }
}

internal class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.Tabla("usuarios", "id_usuario");
        b.Property(x => x.Nombre).HasMaxLength(150);
        b.Property(x => x.NombreUsuario).HasColumnName("usuario").HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.PasswordHash).HasMaxLength(500);
        b.HasIndex(x => x.NombreUsuario).IsUnique();
        b.HasIndex(x => x.Email).IsUnique().HasFilter("email IS NOT NULL");
        b.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.IdEmpresa);
        b.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.IdRol);
    }
}

internal class RolConfig : IEntityTypeConfiguration<Rol>
{
    public void Configure(EntityTypeBuilder<Rol> b)
    {
        b.Tabla("roles", "id_rol");
        b.Property(x => x.Nombre).HasMaxLength(50);
        b.Property(x => x.Descripcion).HasMaxLength(200);
        b.HasIndex(x => x.Nombre).IsUnique();
    }
}

internal class PermisoConfig : IEntityTypeConfiguration<Permiso>
{
    public void Configure(EntityTypeBuilder<Permiso> b)
    {
        b.Tabla("permisos", "id_permiso");
        b.Property(x => x.Nombre).HasMaxLength(100);
        b.Property(x => x.Descripcion).HasMaxLength(200);
        b.HasIndex(x => x.Nombre).IsUnique();
    }
}

internal class RolPermisoConfig : IEntityTypeConfiguration<RolPermiso>
{
    public void Configure(EntityTypeBuilder<RolPermiso> b)
    {
        b.ToTable("roles_permisos");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_rol_permiso").ValueGeneratedNever();
        b.HasOne(x => x.Rol).WithMany(r => r.Permisos).HasForeignKey(x => x.IdRol).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Permiso).WithMany().HasForeignKey(x => x.IdPermiso).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.IdRol, x.IdPermiso }).IsUnique();
    }
}

internal class SesionUsuarioConfig : IEntityTypeConfiguration<SesionUsuario>
{
    public void Configure(EntityTypeBuilder<SesionUsuario> b)
    {
        b.ToTable("sesiones_usuario");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_sesion").ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(128);
        b.Property(x => x.DireccionIp).HasMaxLength(45);
        b.Property(x => x.Dispositivo).HasMaxLength(300);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SesionUsuario>().WithMany().HasForeignKey(x => x.IdReemplazadaPor);
    }
}

internal class AuditoriaConfig : IEntityTypeConfiguration<Auditoria>
{
    public void Configure(EntityTypeBuilder<Auditoria> b)
    {
        b.ToTable("auditoria");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_auditoria").UseIdentityAlwaysColumn();
        b.Property(x => x.TablaAfectada).HasMaxLength(100);
        b.Property(x => x.ValorAnterior).HasColumnType("jsonb");
        b.Property(x => x.ValorNuevo).HasColumnType("jsonb");
        b.Property(x => x.DireccionIp).HasMaxLength(45);
        b.HasOne<Empresa>().WithMany().HasForeignKey(x => x.IdEmpresa);
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuario);
        b.HasIndex(x => new { x.TablaAfectada, x.IdRegistro });
        b.HasIndex(x => new { x.IdEmpresa, x.FechaAccion });
    }
}

internal class ConfiguracionSistemaConfig : IEntityTypeConfiguration<ConfiguracionSistema>
{
    public void Configure(EntityTypeBuilder<ConfiguracionSistema> b)
    {
        b.Tabla("configuracion_sistema", "id_configuracion");
        b.Property(x => x.Clave).HasMaxLength(100);
        b.HasIndex(x => new { x.IdEmpresa, x.Clave }).IsUnique();
    }
}
