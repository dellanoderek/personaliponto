---
titulo: Funcionários
rota: /funcionarios
perfis: [AdminEmpresa, RH, Gestor]
palavras_chave: [funcionário, cadastro, PIN, acesso ao aplicativo, senha temporária, escala, desligamento, cerca virtual, off-line]
---
# Funcionários

## Para que serve
Cadastro dos trabalhadores. Inclusões, alterações e desligamentos geram automaticamente os registros exigidos no AFD.

## Passo a passo
**Cadastrar**
1. Clique em **Novo funcionário**.
2. Aba **Dados**: nome, CPF, matrícula, matrícula eSocial, admissão, cargo, e-mail, local, jornada, cerca virtual e as opções **Marca pelo aplicativo**, **Permite marcação off-line** e **Ativo**. Clique em **Salvar**.

**PIN e acesso** (após salvar, clique em **Editar**)
1. **PIN do terminal:** digite de 4 a 8 dígitos e clique em **Definir PIN**. É usado com matrícula ou CPF no terminal web.
2. **Acesso ao aplicativo:** clique em **Criar acesso**. Aparecem o login e a senha temporária — anote e entregue ao funcionário; ela não é exibida de novo.

**Trocar jornada (Escala)**
1. Aba **Escala**: escolha a nova jornada e a data **A partir de**. Clique em **Aplicar**. O histórico anterior é preservado.

**Desligar**
1. Edite, informe a data de **Desligamento** e desmarque **Ativo**.

## Dúvidas comuns
- **Selo "sem PIN":** o funcionário ainda não pode usar o terminal web.
- **Selo "app":** o funcionário já tem login.
- **Funcionário esqueceu a senha:** gere nova senha em **Usuários**.
- **Cerca "Padrão do local"** usa a regra do estabelecimento; também pode desativar, apenas sinalizar ou bloquear fora da cerca.

## Regras importantes
- O PIN é guardado só como hash e bloqueia após tentativas erradas.
- O Gestor apenas consulta; cadastro e edição são do RH/Administrador.
