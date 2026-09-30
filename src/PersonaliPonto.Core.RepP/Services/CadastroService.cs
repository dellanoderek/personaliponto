using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;

namespace PersonaliPonto.Core.RepP.Services;

/// <summary>
/// Cadastros do núcleo (empresas, estabelecimentos, funcionários, jornadas). Toda alteração que o REP
/// precisa registrar gera o registro correspondente no ARP (tipos 2 e 5) e é auditada.
/// </summary>
public sealed class CadastroService(IRepPDbContext db, RegistroRepService registros, AuditService audit, ICurrentUser user, ITenantContext tenant)
{
    private Guid TenantId => tenant.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.");

    // ---------- Empregador ----------
    public async Task<Empregador> SalvarEmpregadorAsync(Empregador dados, CancellationToken ct)
    {
        ValidarIdentificador(dados.TipoIdentificador, dados.Identificador);
        if (string.IsNullOrWhiteSpace(dados.RazaoSocial)) throw new RegraNegocioException("Razão social obrigatória.");
        dados.Identificador = Documentos.Normalizar(dados.Identificador);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existente = dados.Id == Guid.Empty ? null : await db.Empregadores.FirstOrDefaultAsync(e => e.Id == dados.Id, ct);
        Empregador alvo;
        var razaoMudou = false;
        if (existente is null)
        {
            alvo = new Empregador { TenantId = TenantId };
            db.Empregadores.Add(alvo);
        }
        else
        {
            alvo = existente;
            razaoMudou = alvo.RazaoSocial != dados.RazaoSocial;
        }
        alvo.RazaoSocial = dados.RazaoSocial.Trim();
        alvo.NomeFantasia = dados.NomeFantasia?.Trim();
        alvo.TipoIdentificador = dados.TipoIdentificador;
        alvo.Identificador = dados.Identificador;
        alvo.Ativo = dados.Ativo;
        await db.SaveChangesAsync(ct);

        if (razaoMudou)
        {
            // A razão social compõe o registro tipo 2 de cada estabelecimento.
            var ests = await db.Estabelecimentos.Where(e => e.EmpregadorId == alvo.Id && e.Ativo).ToListAsync(ct);
            foreach (var e in ests) await registros.RegistrarEmpresaAsync(e, alvo, user.Cpf, ct);
        }

        audit.Registrar(existente is null ? "empregador.criado" : "empregador.alterado", nameof(Empregador), alvo.Id,
            new { alvo.RazaoSocial, alvo.Identificador });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return alvo;
    }

    // ---------- Estabelecimento ----------
    public async Task<Estabelecimento> SalvarEstabelecimentoAsync(Estabelecimento dados, CancellationToken ct)
    {
        ValidarIdentificador(dados.TipoIdentificador, dados.Identificador);
        if (string.IsNullOrWhiteSpace(dados.Nome)) throw new RegraNegocioException("Nome obrigatório.");
        if (string.IsNullOrWhiteSpace(dados.LocalPrestacao)) throw new RegraNegocioException("Local de prestação de serviços obrigatório.");
        try { TimeZoneInfo.FindSystemTimeZoneById(dados.FusoHorario); }
        catch { throw new RegraNegocioException("Fuso horário inválido."); }

        var emp = await db.Empregadores.FirstOrDefaultAsync(e => e.Id == dados.EmpregadorId, ct)
                  ?? throw new RegraNegocioException("Empresa não encontrada.");
        var ident = Documentos.Normalizar(dados.Identificador);
        if (await db.Estabelecimentos.AnyAsync(e => e.Identificador == ident && e.Id != dados.Id, ct))
            throw new RegraNegocioException("Já existe estabelecimento com este CNPJ/CPF.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existente = dados.Id == Guid.Empty ? null : await db.Estabelecimentos.FirstOrDefaultAsync(e => e.Id == dados.Id, ct);
        var alvo = existente ?? new Estabelecimento { TenantId = TenantId };
        var identificacaoMudou = existente is null
                                 || alvo.Identificador != ident
                                 || alvo.TipoIdentificador != dados.TipoIdentificador
                                 || alvo.CnoCaepf != Documentos.Normalizar(dados.CnoCaepf)
                                 || alvo.LocalPrestacao != dados.LocalPrestacao.Trim()
                                 || alvo.EmpregadorId != dados.EmpregadorId;

        alvo.EmpregadorId = dados.EmpregadorId;
        alvo.Nome = dados.Nome.Trim();
        alvo.TipoIdentificador = dados.TipoIdentificador;
        alvo.Identificador = ident;
        alvo.CnoCaepf = string.IsNullOrWhiteSpace(dados.CnoCaepf) ? null : Documentos.Normalizar(dados.CnoCaepf);
        alvo.LocalPrestacao = dados.LocalPrestacao.Trim();
        alvo.FusoHorario = dados.FusoHorario;
        alvo.Latitude = dados.Latitude;
        alvo.Longitude = dados.Longitude;
        alvo.RaioCercaMetros = dados.RaioCercaMetros;
        alvo.ModoCerca = dados.ModoCerca;
        alvo.Ativo = dados.Ativo;
        if (existente is null) db.Estabelecimentos.Add(alvo);
        await db.SaveChangesAsync(ct);

        if (identificacaoMudou) await registros.RegistrarEmpresaAsync(alvo, emp, user.Cpf, ct);

        audit.Registrar(existente is null ? "estabelecimento.criado" : "estabelecimento.alterado", nameof(Estabelecimento), alvo.Id,
            new { alvo.Nome, alvo.Identificador, alvo.LocalPrestacao });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return alvo;
    }

    // ---------- Funcionário ----------
    public async Task<Funcionario> SalvarFuncionarioAsync(Funcionario dados, CancellationToken ct)
    {
        var cpf = Documentos.Normalizar(dados.Cpf);
        if (!Documentos.CpfValido(cpf)) throw new RegraNegocioException("CPF inválido.");
        if (string.IsNullOrWhiteSpace(dados.Nome)) throw new RegraNegocioException("Nome obrigatório.");
        if (string.IsNullOrWhiteSpace(dados.Matricula)) throw new RegraNegocioException("Matrícula obrigatória.");
        if (!await db.Estabelecimentos.AnyAsync(e => e.Id == dados.EstabelecimentoId, ct))
            throw new RegraNegocioException("Estabelecimento não encontrado.");
        if (dados.JornadaId is { } jid && !await db.Jornadas.AnyAsync(j => j.Id == jid, ct))
            throw new RegraNegocioException("Jornada não encontrada.");
        if (await db.Funcionarios.AnyAsync(f => f.Id != dados.Id && f.EstabelecimentoId == dados.EstabelecimentoId
                                                && (f.Matricula == dados.Matricula.Trim() || (f.Cpf == cpf && f.Ativo)), ct))
            throw new RegraNegocioException("Já existe funcionário ativo com esta matrícula ou CPF neste estabelecimento.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existente = dados.Id == Guid.Empty ? null : await db.Funcionarios.FirstOrDefaultAsync(f => f.Id == dados.Id, ct);
        var alvo = existente ?? new Funcionario { TenantId = TenantId };
        char? operacao = null;
        if (existente is null) operacao = 'I';
        else if (existente.EstabelecimentoId != dados.EstabelecimentoId)
        {
            // Transferência: exclusão no REP de origem e inclusão no de destino.
            await registros.RegistrarEmpregadoAsync(existente, 'E', user.Cpf, ct);
            operacao = 'I';
        }
        else if (existente.Cpf != cpf || existente.Nome != dados.Nome.Trim()) operacao = 'A';

        var desativando = existente is { Ativo: true } && !dados.Ativo;
        var reativando = existente is { Ativo: false } && dados.Ativo;

        alvo.EstabelecimentoId = dados.EstabelecimentoId;
        alvo.Nome = dados.Nome.Trim();
        alvo.Cpf = cpf;
        alvo.Matricula = dados.Matricula.Trim();
        alvo.MatriculaEsocial = dados.MatriculaEsocial?.Trim();
        alvo.DataAdmissao = dados.DataAdmissao;
        alvo.DataDemissao = dados.DataDemissao;
        alvo.Cargo = dados.Cargo?.Trim();
        alvo.Email = dados.Email?.Trim();
        alvo.JornadaId = dados.JornadaId;
        alvo.GestorId = dados.GestorId;
        alvo.PermiteMarcacaoApp = dados.PermiteMarcacaoApp;
        alvo.PermiteMarcacaoOffline = dados.PermiteMarcacaoOffline;
        alvo.ModoCercaFuncionario = dados.ModoCercaFuncionario;
        alvo.Ativo = dados.Ativo;
        if (existente is null) db.Funcionarios.Add(alvo);
        await db.SaveChangesAsync(ct);

        if (desativando) await registros.RegistrarEmpregadoAsync(alvo, 'E', user.Cpf, ct);
        else if (reativando) await registros.RegistrarEmpregadoAsync(alvo, 'I', user.Cpf, ct);
        else if (operacao is { } op && alvo.Ativo) await registros.RegistrarEmpregadoAsync(alvo, op, user.Cpf, ct);

        audit.Registrar(existente is null ? "funcionario.criado" : "funcionario.alterado", nameof(Funcionario), alvo.Id,
            new { alvo.Nome, Cpf = Documentos.MascararCpf(alvo.Cpf), alvo.Matricula, alvo.Ativo });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return alvo;
    }

    public async Task DefinirPinAsync(Guid funcionarioId, string pin, CancellationToken ct)
    {
        if (!SecretHasher.PinAceitavel(pin))
            throw new RegraNegocioException("PIN deve ter de 4 a 8 dígitos e não pode ser sequência ou repetição.");
        var f = await db.Funcionarios.FirstOrDefaultAsync(x => x.Id == funcionarioId, ct)
                ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        f.PinHash = SecretHasher.Hash(pin);
        f.PinTentativasFalhas = 0;
        f.PinBloqueadoAte = null;
        audit.Registrar("funcionario.pin_definido", nameof(Funcionario), f.Id);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Jornada ----------
    public async Task<Jornada> SalvarJornadaAsync(Jornada dados, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dados.Codigo)) throw new RegraNegocioException("Código obrigatório.");
        if (string.IsNullOrWhiteSpace(dados.Nome)) throw new RegraNegocioException("Nome obrigatório.");
        if (dados.Codigo.Length > 20) throw new RegraNegocioException("Código com no máximo 20 caracteres.");
        if (dados.CicloDias is > 0 && dados.DataReferenciaCiclo is null)
            throw new RegraNegocioException("Escala cíclica exige data de referência.");
        foreach (var h in dados.Horarios)
            if (h.Periodos.Any(p => p.Entrada == p.Saida))
                throw new RegraNegocioException("Período com entrada igual à saída.");
        if (await db.Jornadas.AnyAsync(j => j.Codigo == dados.Codigo.Trim() && j.Id != dados.Id, ct))
            throw new RegraNegocioException("Já existe jornada com este código.");

        var existente = dados.Id == Guid.Empty ? null : await db.Jornadas.FirstOrDefaultAsync(j => j.Id == dados.Id, ct);
        var alvo = existente ?? new Jornada { TenantId = TenantId };
        alvo.Codigo = dados.Codigo.Trim().ToUpperInvariant();
        alvo.Nome = dados.Nome.Trim();
        alvo.Tipo = dados.Tipo;
        alvo.CicloDias = dados.CicloDias;
        alvo.DataReferenciaCiclo = dados.DataReferenciaCiclo;
        alvo.ToleranciaMinutos = dados.ToleranciaMinutos;
        alvo.ToleranciaDiariaMinutos = dados.ToleranciaDiariaMinutos;
        alvo.Horarios = dados.Horarios.Select(h => new HorarioDia
        {
            Indice = h.Indice,
            Periodos = h.Periodos.OrderBy(p => p.Entrada).Select(p => new Periodo { Entrada = p.Entrada, Saida = p.Saida }).ToList()
        }).ToList();
        alvo.Ativo = dados.Ativo;
        if (existente is null) db.Jornadas.Add(alvo);
        audit.Registrar(existente is null ? "jornada.criada" : "jornada.alterada", nameof(Jornada), alvo.Id, new { alvo.Codigo, alvo.Nome });
        await db.SaveChangesAsync(ct);
        return alvo;
    }

    /// <summary>Modelos prontos: turno fixo (seg–sex), 5x2, 6x1 e 12x36.</summary>
    public static Jornada Modelo(TipoJornada tipo, string codigo, string nome)
    {
        static HorarioDia Dia(int i, params (int h1, int m1, int h2, int m2)[] p) => new()
        {
            Indice = i,
            Periodos = p.Select(x => new Periodo { Entrada = new TimeOnly(x.h1, x.m1), Saida = new TimeOnly(x.h2, x.m2) }).ToList()
        };
        var j = new Jornada { Codigo = codigo, Nome = nome, Tipo = tipo };
        switch (tipo)
        {
            case TipoJornada.Fixa:
            case TipoJornada.Semanal5x2:
                for (var d = 1; d <= 5; d++) j.Horarios.Add(Dia(d, (8, 0, 12, 0), (13, 0, 17, 48)));
                break;
            case TipoJornada.Semanal6x1:
                for (var d = 1; d <= 6; d++) j.Horarios.Add(Dia(d, (8, 0, 12, 0), (13, 0, 16, 20)));
                break;
            case TipoJornada.Escala12x36:
                j.CicloDias = 2;
                j.DataReferenciaCiclo = new DateOnly(2026, 1, 1);
                j.Horarios.Add(Dia(0, (7, 0, 12, 0), (13, 0, 19, 0)));
                break;
        }
        return j;
    }

    private static void ValidarIdentificador(TipoIdentificador tipo, string valor)
    {
        var ok = tipo == TipoIdentificador.Cnpj ? Documentos.CnpjValido(valor) : Documentos.CpfValido(valor);
        if (!ok) throw new RegraNegocioException(tipo == TipoIdentificador.Cnpj ? "CNPJ inválido." : "CPF inválido.");
    }
}
