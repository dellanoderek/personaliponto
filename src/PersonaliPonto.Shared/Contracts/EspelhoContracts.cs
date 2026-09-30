namespace PersonaliPonto.Shared.Contracts;

public sealed record EspelhoMarcacaoDto(DateTimeOffset DataHora, string Fonte, bool Desconsiderada, string? Motivo, long? Nsr, Guid? RegistroId = null);

public sealed record EspelhoDiaDto(
    DateOnly Data,
    string? HorarioContratual,
    int MinutosPrevistos,
    int MinutosTrabalhados,
    int MinutosNoturnos,
    int MinutosExtras,
    int MinutosAtraso,
    int MinutosFalta,
    bool Inconsistente,
    string? Ocorrencia,
    IReadOnlyList<EspelhoMarcacaoDto> Marcacoes);

public sealed record EspelhoDto(
    string Empregador,
    string EmpregadorIdentificador,
    string Funcionario,
    string Cpf,
    DateOnly? DataAdmissao,
    string? Cargo,
    string? Jornada,
    DateOnly Inicio,
    DateOnly Fim,
    DateTimeOffset EmitidoEm,
    IReadOnlyList<EspelhoDiaDto> Dias,
    int TotalTrabalhado,
    int TotalExtras,
    int TotalAtrasos,
    int TotalFaltas,
    int SaldoBancoHoras);

public sealed record SolicitacaoCorrecaoRequest(DateOnly Data, TimeOnly Horario, string Motivo);

public sealed record SolicitacaoDto(Guid Id, DateOnly Data, TimeOnly Horario, string Motivo, string Status, string? RespostaRh, DateTimeOffset CriadaEm);
