using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Rotinas;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;

namespace PersonaliPonto.Infrastructure.Pagamentos;

/// <summary>
/// Emite no gateway a cobrança de uma fatura (mensagem do outbox, modo sistema, com retentativa exponencial).
/// Decide a conta: faturas recebidas pelo Owner → conta da plataforma (configuração); faturas recebidas por um
/// revendedor (de parceiro ou de cliente) → conta Asaas conectada do revendedor, se Premium ativo. Cliente público
/// nunca é cobrado automaticamente. Idempotente: reaproveita cobrança já criada com a mesma referência externa.
/// </summary>
public sealed class CobrancaGatewayHandler(
    PersonaliPontoDbContext db, IGatewayPagamento gateway, CredenciaisGateway credenciais, FaturamentoCanalService faturamento,
    IOptions<AsaasOptions> opcoes, ILogger<CobrancaGatewayHandler> log) : IOutboxHandler
{
    public string Tipo => PedidoCobrancaGateway.TipoMensagem;

    public async Task ProcessarAsync(OutboxMessage mensagem, CancellationToken ct)
    {
        var pedido = JsonSerializer.Deserialize<PedidoCobrancaGateway>(mensagem.Payload)
                     ?? throw new InvalidOperationException("Mensagem de cobrança inválida.");
        await EmitirAsync(pedido, ct);
    }

    private sealed record Alvo(ICobrancaGateway Fatura, ClienteGateway Pagador, string Descricao, bool DaPlataforma, ContaGatewayCanal? Conta);

    public async Task EmitirAsync(PedidoCobrancaGateway pedido, CancellationToken ct)
    {
        var alvo = pedido.Origem == PedidoCobrancaGateway.FaturaCanal ? await AlvoCanalAsync(pedido.FaturaId, ct) : await AlvoClienteAsync(pedido.FaturaId, ct);
        if (alvo is null) return;
        var f = alvo.Fatura;
        if (f.Status != StatusFatura.Aberta || f.Valor <= 0 || f.GatewayStatus == StatusCobrancaGateway.Emitida && f.GatewayCobrancaId is not null)
        {
            if (f.GatewayStatus == StatusCobrancaGateway.Pendente) f.GatewayStatus = StatusCobrancaGateway.Nenhuma;
            return;
        }

        var cred = alvo.DaPlataforma ? credenciais.Owner() : alvo.Conta is { } conta ? credenciais.DaConta(conta) : null;
        if (cred is null)
        {
            f.GatewayStatus = StatusCobrancaGateway.Nenhuma; // sem conta: fatura manual, como antes
            return;
        }

        var referencia = $"{pedido.Origem}:{f.Id}";
        try
        {
            var cobranca = await gateway.BuscarCobrancaAsync(cred, referencia, ct);
            if (cobranca is null)
            {
                var clienteId = await gateway.GarantirClienteAsync(cred, alvo.Pagador, ct);
                var hoje = faturamento.Hoje;
                var venc = f.Vencimento < hoje ? hoje : f.Vencimento; // o Asaas recusa vencimento no passado
                cobranca = await gateway.CriarCobrancaAsync(cred, new NovaCobranca(clienteId, f.Valor, venc, alvo.Descricao, referencia), ct);
            }
            f.GatewayCobrancaId = cobranca.Id;
            f.GatewayContaId = alvo.Conta?.Id;
            f.GatewayLink = cobranca.LinkFatura;
            f.GatewayBoletoUrl = cobranca.BoletoUrl;
            f.GatewayStatus = StatusCobrancaGateway.Emitida;
            f.GatewayErro = null;

            // Pix e linha digitável são complementares: falha aqui não desfaz a cobrança (o link cobre as três formas).
            try
            {
                if (await gateway.ObterPixAsync(cred, cobranca.Id, ct) is { } pix)
                {
                    f.GatewayPixCopiaCola = pix.CopiaECola.Length > 1000 ? null : pix.CopiaECola;
                    f.GatewayPixQrCode = pix.QrCodeBase64;
                }
            }
            catch (GatewayPagamentoException ex) { log.LogWarning("Pix da cobrança {Cobranca} indisponível: {Erro}", cobranca.Id, ex.Message); }
            try { f.GatewayLinhaDigitavel = await gateway.ObterLinhaDigitavelAsync(cred, cobranca.Id, ct); }
            catch (GatewayPagamentoException ex) { log.LogWarning("Linha digitável da cobrança {Cobranca} indisponível: {Erro}", cobranca.Id, ex.Message); }

            var nf = opcoes.Value.NotaFiscal;
            if (alvo.DaPlataforma && nf.Habilitada)
            {
                try
                {
                    await gateway.AgendarNotaFiscalAsync(cred, new NotaFiscalGateway(cobranca.Id, f.Valor, faturamento.Hoje, nf.DescricaoServico,
                        nf.CodigoServicoMunicipal, nf.NomeServicoMunicipal, nf.AliquotaIss, nf.RetemIss, alvo.Descricao), ct);
                }
                catch (GatewayPagamentoException ex) { log.LogWarning("NFS-e da cobrança {Cobranca} não agendada: {Erro}", cobranca.Id, ex.Message); }
            }
            log.LogInformation("Cobrança {Cobranca} emitida no Asaas para a fatura {Fatura} ({Origem}).", cobranca.Id, f.Id, pedido.Origem);
        }
        catch (GatewayPagamentoException ex)
        {
            f.GatewayStatus = StatusCobrancaGateway.Falha;
            f.GatewayErro = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            throw; // o outbox tenta de novo; a fatura continua válida (pendente de envio)
        }
    }

    private async Task<Alvo?> AlvoCanalAsync(Guid id, CancellationToken ct)
    {
        var f = await db.FaturasCanal.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return null;
        var pagador = await db.Canais.AsNoTracking().FirstAsync(c => c.Id == f.CanalPagadorId, ct);
        var cliente = new ClienteGateway(pagador.RazaoSocial, pagador.Cnpj, pagador.Email, pagador.Telefone, $"canal:{pagador.Id}");
        var desc = f.Tipo switch
        {
            TipoFaturaCanal.Plataforma => $"PersonaliPonto — uso da plataforma {f.Competencia:MM/yyyy}",
            TipoFaturaCanal.Parceiro => $"Parceria — funcionários ativos {f.Competencia:MM/yyyy}",
            _ => "PersonaliPonto — compra no painel"
        };
        if (f.CanalRecebedorId == Canal.OwnerRaizId) return new Alvo(f, cliente, desc, true, null);
        return new Alvo(f, cliente, desc, false, await ContaAtivaPremiumAsync(f.CanalRecebedorId, ct));
    }

    private async Task<Alvo?> AlvoClienteAsync(Guid id, CancellationToken ct)
    {
        var f = await db.Faturas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return null;
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == f.TenantId, ct);
        var assinatura = await db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == f.TenantId, ct);
        var cliente = new ClienteGateway(assinatura?.RazaoSocial ?? tenant.Nome, assinatura?.Cnpj ?? "", assinatura?.Email ?? tenant.EmailContato,
            assinatura?.Telefone ?? tenant.TelefoneContato, $"cliente:{tenant.Id}");
        var desc = $"Sistema de ponto — mensalidade {f.Competencia:MM/yyyy}";
        // Cliente público: nunca há cobrança automática (fatura manual/empenho).
        if (tenant.TipoEntidade == TipoEntidade.Publica || string.IsNullOrWhiteSpace(cliente.CpfCnpj)) return new Alvo(f, cliente, desc, false, null);
        var dono = await db.Canais.AsNoTracking().FirstAsync(c => c.Id == tenant.CanalDonoId, ct);
        // Só o revendedor Premium com conta própria cobra os clientes automaticamente (o Owner não cobra clientes pelo Asaas nesta etapa).
        var conta = dono.Tipo == TipoCanal.Revendedor ? await ContaAtivaPremiumAsync(dono.Id, ct) : null;
        return new Alvo(f, cliente, desc, false, conta);
    }

    private async Task<ContaGatewayCanal?> ContaAtivaPremiumAsync(Guid canalId, CancellationToken ct)
    {
        var conta = await db.ContasGateway.AsNoTracking().FirstOrDefaultAsync(c => c.CanalId == canalId && c.Ativa, ct);
        return conta is not null && await faturamento.PremiumAtivoAsync(canalId, ct) ? conta : null;
    }
}

/// <summary>
/// Varredura da rotina de plataforma: faturas marcadas como pendentes de envio sem mensagem viva no outbox (geradas
/// fora do modo sistema, p.ex. cliente cadastrado pelo canal) vão para a fila. Idempotente.
/// </summary>
public sealed class VarreduraCobrancasGateway(PersonaliPontoDbContext db, Core.RepP.Abstractions.IClock clock)
{
    public async Task<int> EnfileirarPendentesAsync(CancellationToken ct)
    {
        var limite = clock.UtcNow.AddMinutes(-10);
        var vivas = (await db.Outbox.AsNoTracking()
                .Where(m => m.Tipo == PedidoCobrancaGateway.TipoMensagem && m.ProcessadoEm == null && m.Tentativas < 10)
                .Select(m => m.Payload).ToListAsync(ct))
            .Select(p => JsonSerializer.Deserialize<PedidoCobrancaGateway>(p)?.FaturaId).ToHashSet();
        // Só pendentes: falhas já têm retentativa exponencial no outbox (evita martelar o Asaas com chave inválida).
        var estados = new[] { StatusCobrancaGateway.Pendente };
        var canal = await db.FaturasCanal.AsNoTracking()
            .Where(f => estados.Contains(f.GatewayStatus) && f.Status == StatusFatura.Aberta && f.CriadaEm < limite)
            .Select(f => f.Id).Take(200).ToListAsync(ct);
        var clientes = await db.Faturas.AsNoTracking()
            .Where(f => estados.Contains(f.GatewayStatus) && f.Status == StatusFatura.Aberta && f.CriadaEm < limite)
            .Select(f => new { f.Id, f.TenantId }).Take(200).ToListAsync(ct);
        var n = 0;
        foreach (var id in canal.Where(id => !vivas.Contains(id)))
        {
            db.Outbox.Add(FilaCobrancaGateway.Mensagem(new PedidoCobrancaGateway(PedidoCobrancaGateway.FaturaCanal, id), null, clock));
            n++;
        }
        foreach (var f in clientes.Where(f => !vivas.Contains(f.Id)))
        {
            db.Outbox.Add(FilaCobrancaGateway.Mensagem(new PedidoCobrancaGateway(PedidoCobrancaGateway.FaturaCliente, f.Id), f.TenantId, clock));
            n++;
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }
}
