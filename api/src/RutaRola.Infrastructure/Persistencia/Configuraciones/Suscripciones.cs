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
    }
}
