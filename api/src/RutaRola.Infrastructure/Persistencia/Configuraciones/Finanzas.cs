using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Finanzas;
using static RutaRola.Infrastructure.Persistencia.Configuraciones.Extensiones;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class FacturaConfig : IEntityTypeConfiguration<Factura>
{
    public void Configure(EntityTypeBuilder<Factura> b)
    {
        b.Tabla("facturas", "id_factura");
        b.Property(x => x.NumeroFactura).HasMaxLength(20);
        b.Property(x => x.NombreAcudiente).HasMaxLength(200);
        b.Property(x => x.NombreEstudiante).HasMaxLength(200);
        b.Property(x => x.ValorMensual).HasColumnType(Dinero);
        b.Property(x => x.ValorTotal).HasColumnType(Dinero);
        b.Property(x => x.Concepto).HasMaxLength(200);
        b.Property(x => x.UrlPdf).HasMaxLength(500);
        b.HasOne(x => x.Acudiente).WithMany().HasForeignKey(x => x.IdAcudiente);
        b.HasOne(x => x.Estudiante).WithMany().HasForeignKey(x => x.IdEstudiante);
        b.HasIndex(x => new { x.IdEmpresa, x.NumeroFactura }).IsUnique();
    }
}

internal class FacturaDetalleConfig : IEntityTypeConfiguration<FacturaDetalle>
{
    public void Configure(EntityTypeBuilder<FacturaDetalle> b)
    {
        b.ToTable("facturas_detalle");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_detalle").ValueGeneratedNever();
        b.Property(x => x.Descripcion).HasMaxLength(200);
        b.Property(x => x.PrecioUnitario).HasColumnType(Dinero);
        b.Property(x => x.Total).HasColumnType(Dinero);
        b.HasOne(x => x.Factura).WithMany(f => f.Detalle).HasForeignKey(x => x.IdFactura).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class PagoConfig : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> b)
    {
        b.Tabla("pagos", "id_pago");
        b.Property(x => x.Numero).HasMaxLength(20);
        b.Property(x => x.Valor).HasColumnType(Dinero);
        b.Property(x => x.NumeroReferencia).HasMaxLength(100);
        b.Property(x => x.NumeroFactura).HasMaxLength(20);
        b.Property(x => x.NombreAcudiente).HasMaxLength(200);
        b.Property(x => x.NombreEstudiante).HasMaxLength(200);
        b.HasOne(x => x.Estudiante).WithMany().HasForeignKey(x => x.IdEstudiante);
        b.HasOne(x => x.Factura).WithMany().HasForeignKey(x => x.IdFactura);
        b.HasIndex(x => new { x.IdEmpresa, x.Numero }).IsUnique();
        // Un solo recibo vigente por factura y mes.
        b.HasIndex(x => new { x.IdFactura, x.Periodo }).IsUnique()
            .HasFilter("estado = 'VIGENTE' AND id_factura IS NOT NULL").HasDatabaseName("ix_pagos_vigente_por_periodo");
        b.HasIndex(x => x.IdFactura);
        b.HasIndex(x => new { x.IdEmpresa, x.FechaPago });
    }
}

internal class CotizacionConfig : IEntityTypeConfiguration<Cotizacion>
{
    public void Configure(EntityTypeBuilder<Cotizacion> b)
    {
        b.Tabla("cotizaciones", "id_cotizacion");
        b.Property(x => x.Numero).HasMaxLength(20);
        b.Property(x => x.NombreAcudiente).HasMaxLength(200);
        b.Property(x => x.TelefonoAcudiente).HasMaxLength(20);
        b.Property(x => x.ValorPorEstudiante).HasColumnType(Dinero);
        b.Property(x => x.ValorTotal).HasColumnType(Dinero);
        b.HasOne(x => x.Acudiente).WithMany().HasForeignKey(x => x.IdAcudiente);
        b.HasIndex(x => new { x.IdEmpresa, x.Numero }).IsUnique();
    }
}

internal class CotizacionEstudianteConfig : IEntityTypeConfiguration<CotizacionEstudiante>
{
    public void Configure(EntityTypeBuilder<CotizacionEstudiante> b)
    {
        b.ToTable("cotizaciones_estudiantes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_cotizacion_estudiante").ValueGeneratedNever();
        b.Property(x => x.Nombre).HasMaxLength(200);
        b.HasOne(x => x.Cotizacion).WithMany(c => c.Estudiantes).HasForeignKey(x => x.IdCotizacion).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Estudiante).WithMany().HasForeignKey(x => x.IdEstudiante);
    }
}

internal class GastoConfig : IEntityTypeConfiguration<Gasto>
{
    public void Configure(EntityTypeBuilder<Gasto> b)
    {
        b.Tabla("gastos", "id_gasto");
        b.Property(x => x.Categoria).HasMaxLength(50);
        b.Property(x => x.Valor).HasColumnType(Dinero);
        b.HasOne(x => x.Vehiculo).WithMany().HasForeignKey(x => x.IdVehiculo);
        b.HasOne(x => x.Creador).WithMany().HasForeignKey(x => x.CreadoPor);
        b.HasIndex(x => new { x.IdEmpresa, x.FechaGasto });
    }
}

internal class ProgramacionCobroConfig : IEntityTypeConfiguration<ProgramacionCobro>
{
    public void Configure(EntityTypeBuilder<ProgramacionCobro> b)
    {
        b.Tabla("programaciones_cobro", "id_programacion");
        b.Property(x => x.Nombre).HasMaxLength(100);
        b.HasOne(x => x.Factura).WithMany().HasForeignKey(x => x.IdFactura);
        // Una programación por factura.
        b.HasIndex(x => x.IdFactura).IsUnique();
    }
}

internal class PlantillaMensajeConfig : IEntityTypeConfiguration<PlantillaMensaje>
{
    public void Configure(EntityTypeBuilder<PlantillaMensaje> b)
    {
        b.ToTable("plantillas_mensaje");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_plantilla").ValueGeneratedNever();
        b.Property(x => x.Asunto).HasMaxLength(200);
        b.HasOne(x => x.Programacion).WithMany(p => p.Plantillas).HasForeignKey(x => x.IdProgramacion).OnDelete(DeleteBehavior.Cascade);
        // Una plantilla EN_USO por canal: la valida ServicioProgramaciones. No es índice único porque al
        // cambiar cuál está "En uso" las dos filas se actualizan en el mismo guardado y PostgreSQL no
        // permite diferir un índice parcial.
        b.HasIndex(x => new { x.IdProgramacion, x.Canal }).HasFilter("estado = 'EN_USO'")
            .HasDatabaseName("ix_plantillas_mensaje_en_uso");
        b.HasIndex(x => x.IdProgramacion);
    }
}

internal class PlantillaMensajeArchivoConfig : IEntityTypeConfiguration<PlantillaMensajeArchivo>
{
    public void Configure(EntityTypeBuilder<PlantillaMensajeArchivo> b)
    {
        b.ToTable("plantillas_mensaje_archivos");
        b.HasKey(x => new { x.IdPlantilla, x.IdArchivo });
        b.HasOne(x => x.Plantilla).WithMany(p => p.Adjuntos).HasForeignKey(x => x.IdPlantilla).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Archivo).WithMany().HasForeignKey(x => x.IdArchivo);
        b.HasIndex(x => x.IdArchivo);
    }
}

internal class EnvioProgramacionConfig : IEntityTypeConfiguration<EnvioProgramacion>
{
    public void Configure(EntityTypeBuilder<EnvioProgramacion> b)
    {
        b.ToTable("envios_programacion");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_envio").ValueGeneratedNever();
        b.Property(x => x.Destinatario).HasMaxLength(150);
        b.HasOne(x => x.Programacion).WithMany(p => p.Envios).HasForeignKey(x => x.IdProgramacion).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.MensajeWhatsapp).WithMany().HasForeignKey(x => x.IdMensajeWhatsapp);
        b.HasIndex(x => new { x.IdProgramacion, x.Fecha });
    }
}
