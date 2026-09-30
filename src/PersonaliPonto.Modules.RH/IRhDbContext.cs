using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Modules.RH.Domain;

namespace PersonaliPonto.Modules.RH;

public interface IRhDbContext : IRepPDbContext
{
    DbSet<SolicitacaoCorrecao> Solicitacoes { get; }
    DbSet<Atestado> Atestados { get; }
    DbSet<BancoHorasLancamento> BancoHoras { get; }
    DbSet<Feriado> Feriados { get; }
    DbSet<EscalaFuncionario> Escalas { get; }
    DbSet<FechamentoPeriodo> Fechamentos { get; }
    DbSet<PoliticaBancoHoras> PoliticasBancoHoras { get; }
    DbSet<AssinaturaEspelho> AssinaturasEspelho { get; }
    DbSet<ConfiguracaoSeguranca> ConfiguracoesSeguranca { get; }
    DbSet<DispositivoFuncionario> Dispositivos { get; }
    DbSet<FotoMarcacao> FotosMarcacao { get; }
}
