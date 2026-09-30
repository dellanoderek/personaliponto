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
    DbSet<Canal> Canais { get; }
    DbSet<Municipio> Municipios { get; }
    DbSet<TabelaPrecoCanal> TabelasPrecoCanal { get; }
    DbSet<ApuracaoUsoCanal> ApuracoesUsoCanal { get; }
    DbSet<ApuracaoUsoTenant> ApuracoesUsoTenant { get; }
    DbSet<FaturaCanal> FaturasCanal { get; }
    DbSet<ItemFaturaCanal> ItensFaturaCanal { get; }
    DbSet<AssinaturaPremium> AssinaturasPremium { get; }
    DbSet<ContaGatewayCanal> ContasGateway { get; }
    DbSet<EventoGateway> EventosGateway { get; }
    DbSet<MarcaCanal> MarcasCanal { get; }
    DbSet<DominioCanal> DominiosCanal { get; }
}
