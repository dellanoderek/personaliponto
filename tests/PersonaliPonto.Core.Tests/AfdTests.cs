using PersonaliPonto.Core.RepP.Afd;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Core.Tests;

public class AfdTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);

    private static AfdCabecalho Cab() => new(
        TipoIdentificador.Cnpj, "11.222.333/0001-81", null, "EMPRESA EXEMPLO LTDA",
        "BR512026000123-4", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
        Base, -180, TipoIdentificador.Cnpj, "12.ABC.345/01DE-35");

    private static List<RegistroRep> Registros()
    {
        var lista = new List<RegistroRep>
        {
            new()
            {
                Nsr = 1, Tipo = TipoRegistroRep.InclusaoAlteracaoEmpresa, DataHoraGravacao = Base, OffsetGravacaoMinutos = -180,
                CpfResponsavel = "52998224725", TipoIdentificadorEmpregador = TipoIdentificador.Cnpj,
                IdentificadorEmpregador = "11222333000181", RazaoSocial = "EMPRESA EXEMPLO LTDA", LocalPrestacao = "Rua A, 100 - Campina Grande/PB"
            },
            new()
            {
                Nsr = 2, Tipo = TipoRegistroRep.InclusaoAlteracaoExclusaoEmpregado, DataHoraGravacao = Base, OffsetGravacaoMinutos = -180,
                TipoOperacao = 'I', Cpf = "52998224725", NomeEmpregado = "JOSÉ DA SILVA", CpfResponsavel = "52998224725"
            },
            new() { Nsr = 3, Tipo = TipoRegistroRep.EventoSensivel, DataHoraGravacao = Base, OffsetGravacaoMinutos = -180, TipoEvento = 7 }
        };

        string? anterior = null;
        for (var i = 0; i < 3; i++)
        {
            var r = new RegistroRep
            {
                Nsr = 4 + i, Tipo = TipoRegistroRep.MarcacaoRepP,
                DataHoraMarcacao = Base.AddHours(i * 4), OffsetMarcacaoMinutos = -180,
                DataHoraGravacao = Base.AddHours(i * 4).AddSeconds(2), OffsetGravacaoMinutos = -180,
                Cpf = "52998224725", Coletor = i == 2 ? Coletor.AplicativoMobile : Coletor.Browser, Offline = i == 2
            };
            r.Hash = AfdGenerator.CalcularHashTipo7(r, anterior);
            anterior = r.Hash;
            lista.Add(r);
        }
        return lista;
    }

    [Fact]
    public void Arquivo_gerado_passa_no_validador_oficial()
    {
        var arq = AfdGenerator.Gerar(Cab(), Registros());
        Assert.Empty(AfdValidator.Validar(arq.Conteudo));
        Assert.Equal("AFD512026000123411222333000181REP_P.txt", arq.NomeArquivo);
    }

    [Fact]
    public void Cabecalho_posicoes_byte_a_byte()
    {
        var h = AfdGenerator.Cabecalho(Cab());
        Assert.Equal(302, h.Length);
        string C(int ini, int fim) => h.Substring(ini - 1, fim - ini + 1);
        Assert.Equal("000000000", C(1, 9));
        Assert.Equal("1", C(10, 10));
        Assert.Equal("1", C(11, 11));
        Assert.Equal("11222333000181", C(12, 25));
        Assert.Equal(new string(' ', 14), C(26, 39));
        Assert.Equal("EMPRESA EXEMPLO LTDA".PadRight(150), C(40, 189));
        Assert.Equal("5120260001234    ", C(190, 206));
        Assert.Equal("2026-09-01", C(207, 216));
        Assert.Equal("2026-09-30", C(217, 226));
        Assert.Equal("2026-09-01T08:00:00-0300", C(227, 250));
        Assert.Equal("004", C(251, 253));
        Assert.Equal("1", C(254, 254));
        Assert.Equal("12ABC34501DE35", C(255, 268));
        Assert.Equal(new string(' ', 30), C(269, 298));
        Assert.Equal(Crc16Kermit.Hex(AfdCampos.Latin1.GetBytes(h[..298])), C(299, 302));
    }

    [Fact]
    public void Registro_tipo7_posicoes_byte_a_byte()
    {
        var r = Registros()[3];
        var l = AfdGenerator.Linha(r);
        Assert.Equal(137, l.Length);
        string C(int ini, int fim) => l.Substring(ini - 1, fim - ini + 1);
        Assert.Equal("000000004", C(1, 9));
        Assert.Equal("7", C(10, 10));
        Assert.Equal("2026-09-01T08:00:00-0300", C(11, 34));
        Assert.Equal("052998224725", C(35, 46));
        Assert.Equal("2026-09-01T08:00:00-0300", C(47, 70));
        Assert.Equal("02", C(71, 72));
        Assert.Equal("0", C(73, 73));
        Assert.Matches("^[0-9A-F]{64}$", C(74, 137));
    }

    [Fact]
    public void Hash_encadeado_detecta_adulteracao()
    {
        var regs = Registros();
        var arq = AfdGenerator.Gerar(Cab(), regs);
        var texto = AfdCampos.Latin1.GetString(arq.Conteudo);
        // Adultera o horário da segunda marcação (12:00 -> 12:01) mantendo o tamanho.
        var adulterado = texto.Replace("2026-09-01T12:00:00-0300", "2026-09-01T12:01:00-0300");
        Assert.NotEqual(texto, adulterado);
        var erros = AfdValidator.Validar(AfdCampos.Latin1.GetBytes(adulterado));
        Assert.Contains(erros, e => e.Contains("hash"));
    }

    [Fact]
    public void Primeiro_hash_do_estabelecimento_sem_anterior_e_depois_encadeado()
    {
        var regs = Registros().Where(r => r.Tipo == TipoRegistroRep.MarcacaoRepP).ToList();
        Assert.Equal(AfdGenerator.CalcularHashTipo7(regs[0], null), regs[0].Hash);
        Assert.Equal(AfdGenerator.CalcularHashTipo7(regs[1], regs[0].Hash), regs[1].Hash);
        Assert.NotEqual(AfdGenerator.CalcularHashTipo7(regs[1], null), regs[1].Hash);
    }

    [Fact]
    public void Trailer_e_assinatura()
    {
        var arq = AfdGenerator.Gerar(Cab(), Registros());
        var linhas = AfdCampos.Latin1.GetString(arq.Conteudo).Split("\r\n");
        var trailer = linhas[^3];
        Assert.Equal("999999999" + "000000001" + "000000000" + "000000000" + "000000001" + "000000001" + "000000003" + "9", trailer);
        Assert.Equal("ASSINATURA_DIGITAL_EM_ARQUIVO_P7S".PadRight(100), linhas[^2]);
        Assert.Equal("", linhas[^1]);
    }

    [Fact]
    public void Codificacao_iso_8859_1()
    {
        var arq = AfdGenerator.Gerar(Cab(), Registros());
        // "JOSÉ" deve ser 1 byte por caractere (É = 0xC9).
        Assert.Contains((byte)0xC9, arq.Conteudo);
        Assert.DoesNotContain(arq.Conteudo.Zip(arq.Conteudo.Skip(1)), p => p.First == 0xC3 && p.Second == 0x89);
    }

    [Theory]
    [InlineData(TipoRegistroRep.InclusaoAlteracaoEmpresa, 331)]
    [InlineData(TipoRegistroRep.InclusaoAlteracaoExclusaoEmpregado, 118)]
    [InlineData(TipoRegistroRep.EventoSensivel, 36)]
    public void Tamanhos_dos_registros(TipoRegistroRep tipo, int tamanho)
    {
        var r = Registros().First(x => x.Tipo == tipo);
        Assert.Equal(tamanho, AfdGenerator.Linha(r).Length);
    }
}
