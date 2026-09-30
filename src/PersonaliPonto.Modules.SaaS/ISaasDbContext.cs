using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Modules.SaaS.Domain;

namespace PersonaliPonto.Modules.SaaS;

public interface ISaasDbContext : IRepPDbContext
{
    DbSet<Plano> Planos { get; }
    DbSet<Assinatura> Assinaturas { get; }
    DbSet<Fatura> Faturas { get; }
    DbSet<AcessoSuporte> AcessosSuporte { get; }
}
