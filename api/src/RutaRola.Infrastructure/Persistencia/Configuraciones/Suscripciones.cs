using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Suscripciones;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class SuscripcionConfig : IEntityTypeConfiguration<Suscripcion>
{
    public void Configure(EntityTypeBuilder<Suscripcion> b)
    {
        b.Tabla("suscripciones", "id_suscripcion");
        b.Property(x => x.Nota).HasMaxLength(500);
        // Una por empresa.
        b.HasIndex(x => x.IdEmpresa).IsUnique().HasDatabaseName("ix_suscripciones_id_empresa");
    }
}

internal class PagoSuscripcionConfig : IEntityTypeConfiguration<PagoSuscripcion>
{
    public void Configure(EntityTypeBuilder<PagoSuscripcion> b)
    {
        b.Tabla("pagos_suscripcion", "id_pago_suscripcion");
        b.Property(x => x.Referencia).HasMaxLength(60);
        b.Property(x => x.IdTransaccion).HasMaxLength(80);
        b.Property(x => x.MetodoPago).HasMaxLength(40);
        b.Property(x => x.Ambiente).HasMaxLength(10);
        b.Property(x => x.CorreoPagador).HasMaxLength(150);
        b.HasIndex(x => x.Referencia).IsUnique().HasDatabaseName("ix_pagos_suscripcion_referencia");
        b.HasIndex(x => new { x.IdEmpresa, x.FechaCreacion });
        b.Property(x => x.IdCodigoPromocional).HasColumnName("id_codigo_promocional");
        b.Property(x => x.CodigoPromocional).HasMaxLength(30);
        b.HasOne<CodigoPromocional>().WithMany().HasForeignKey(x => x.IdCodigoPromocional);
    }
}

internal class CodigoPromocionalConfig : IEntityTypeConfiguration<CodigoPromocional>
{
    public void Configure(EntityTypeBuilder<CodigoPromocional> b)
    {
        b.Tabla("codigos_promocionales", "id_codigo_promocional");
        b.Property(x => x.Codigo).HasMaxLength(30);
        b.Property(x => x.Descripcion).HasMaxLength(200);
        b.Property(x => x.Valor).HasPrecision(14, 2);
        b.Property(x => x.Planes).HasMaxLength(100);
        b.HasIndex(x => x.Codigo).IsUnique().HasDatabaseName("ix_codigos_promocionales_codigo");
    }
}

internal class CodigoPromocionalEmpresaConfig : IEntityTypeConfiguration<CodigoPromocionalEmpresa>
{
    public void Configure(EntityTypeBuilder<CodigoPromocionalEmpresa> b)
    {
        b.ToTable("codigos_promocionales_empresas");
        b.HasKey(x => new { x.IdCodigoPromocional, x.IdEmpresa });
        b.Property(x => x.IdCodigoPromocional).HasColumnName("id_codigo_promocional");
        b.Property(x => x.IdEmpresa).HasColumnName("id_empresa");
        b.HasOne(x => x.CodigoPromocional).WithMany(c => c.Empresas).HasForeignKey(x => x.IdCodigoPromocional).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<RutaRola.Domain.Seguridad.Empresa>().WithMany().HasForeignKey(x => x.IdEmpresa);
    }
}

internal class UsoCodigoPromocionalConfig : IEntityTypeConfiguration<UsoCodigoPromocional>
{
    public void Configure(EntityTypeBuilder<UsoCodigoPromocional> b)
    {
        b.Tabla("usos_codigo_promocional", "id_uso_codigo_promocional");
        b.Property(x => x.IdCodigoPromocional).HasColumnName("id_codigo_promocional");
        b.Property(x => x.IdPagoSuscripcion).HasColumnName("id_pago_suscripcion");
        b.HasOne(x => x.CodigoPromocional).WithMany().HasForeignKey(x => x.IdCodigoPromocional);
        b.HasOne<PagoSuscripcion>().WithMany().HasForeignKey(x => x.IdPagoSuscripcion);
        b.HasIndex(x => new { x.IdCodigoPromocional, x.IdEmpresa });
    }
}
