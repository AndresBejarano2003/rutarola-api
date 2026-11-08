using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Soporte;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class TicketSoporteConfig : IEntityTypeConfiguration<TicketSoporte>
{
    public void Configure(EntityTypeBuilder<TicketSoporte> b)
    {
        b.Tabla("tickets_soporte", "id_ticket");
        b.Property(x => x.Numero).HasMaxLength(20);
        b.Property(x => x.Modulo).HasMaxLength(100);
        b.Property(x => x.Asunto).HasMaxLength(120);
        b.Property(x => x.Descripcion).HasMaxLength(2000);
        b.Property(x => x.NombreSolicitante).HasMaxLength(150);
        b.Property(x => x.CorreoSolicitante).HasMaxLength(150);
        b.Property(x => x.NombreResponsable).HasMaxLength(150);
        b.Property(x => x.ComentarioCalificacion).HasMaxLength(1000);
        b.HasOne(x => x.Solicitante).WithMany().HasForeignKey(x => x.IdSolicitante);
        b.HasOne(x => x.Responsable).WithMany().HasForeignKey(x => x.IdResponsable);
        // Consecutivo global: la plataforma ve los tickets de todas las empresas.
        b.HasIndex(x => x.Numero).IsUnique();
        b.HasIndex(x => new { x.IdEmpresa, x.FechaCreacion });
        b.HasIndex(x => new { x.Estado, x.FechaUltimaActividad });
    }
}

internal class EventoTicketConfig : IEntityTypeConfiguration<EventoTicket>
{
    public void Configure(EntityTypeBuilder<EventoTicket> b)
    {
        b.ToTable("eventos_ticket");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_evento_ticket").ValueGeneratedNever();
        b.Property(x => x.Texto).HasMaxLength(4000);
        b.Property(x => x.NombreUsuario).HasMaxLength(150);
        b.HasOne(x => x.Ticket).WithMany(t => t.Eventos).HasForeignKey(x => x.IdTicket).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.IdTicket, x.Fecha });
    }
}

internal class ReunionTicketConfig : IEntityTypeConfiguration<ReunionTicket>
{
    public void Configure(EntityTypeBuilder<ReunionTicket> b)
    {
        b.ToTable("reuniones_ticket");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_reunion").ValueGeneratedNever();
        b.Property(x => x.Enlace).HasMaxLength(500);
        b.Property(x => x.Motivo).HasMaxLength(300);
        b.Property(x => x.Notas).HasMaxLength(4000);
        b.Property(x => x.NombreOrganizador).HasMaxLength(150);
        b.HasOne(x => x.Ticket).WithMany(t => t.Reuniones).HasForeignKey(x => x.IdTicket).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Estado, x.Fecha });
    }
}

internal class AdjuntoTicketConfig : IEntityTypeConfiguration<AdjuntoTicket>
{
    public void Configure(EntityTypeBuilder<AdjuntoTicket> b)
    {
        b.ToTable("adjuntos_ticket");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_adjunto").ValueGeneratedNever();
        b.Property(x => x.Nombre).HasMaxLength(255);
        b.Property(x => x.Tipo).HasMaxLength(100);
        b.HasOne(x => x.Ticket).WithMany(t => t.Adjuntos).HasForeignKey(x => x.IdTicket).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Evento).WithMany().HasForeignKey(x => x.IdEvento).OnDelete(DeleteBehavior.Cascade);
    }
}
